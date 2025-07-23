using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sharphound.Client;
using Sharphound.Producers;
using Sharphound.Writers;
using SharpHoundCommonLib;
using SharpHoundCommonLib.Enums;
using SharpHoundCommonLib.OutputTypes;

namespace Sharphound.Runtime
{
    public class CollectionTask
    {
        private readonly Channel<CSVComputerStatus> _compStatusChannel;
        private readonly CompStatusWriter _compStatusWriter;
        private readonly IContext _context;
        private readonly Channel<IDirectoryObject> _ldapChannel;
        private readonly ILogger _log;
        private readonly Channel<OutputBase> _outputChannel;

        private readonly OutputWriter _outputWriter;
        private readonly BaseProducer _producer;
        private readonly List<Task> _taskPool = new();
        private const string EnterpriseDCSuffix = "S-1-5-9";

        public CollectionTask(IContext context)
        {
            _context = context;
            _log = context.Logger;
            _ldapChannel = Channel.CreateBounded<IDirectoryObject>(new BoundedChannelOptions(1000)
            {
                SingleWriter = true,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _compStatusChannel = Channel.CreateUnbounded<CSVComputerStatus>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            if (context.Flags.DumpComputerStatus) _compStatusWriter = new CompStatusWriter(context, _compStatusChannel);

            _outputChannel = Channel.CreateUnbounded<OutputBase>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _outputWriter = new OutputWriter(context, _outputChannel);

            if (context.Flags.Stealth)
                _producer = new StealthProducer(context, _ldapChannel, _outputChannel, _compStatusChannel);
            else if (context.ComputerFile != null)
                _producer = new ComputerFileProducer(context, _ldapChannel, _outputChannel, _compStatusChannel);
            else
                _producer = new LdapProducer(context, _ldapChannel, _outputChannel, _compStatusChannel);
        }

        internal async Task<string> StartCollection()
        {
            for (var i = 0; i < _context.Threads; i++)
            {
                var consumer = ConsumeSearchResults();
                _taskPool.Add(consumer);
            }

            var outputTask = _outputWriter.StartWriter();
            _outputWriter.StartStatusOutput();
            var compStatusTask = _compStatusWriter?.StartWriter();
            var producerTask = _producer.Produce();
            await producerTask;

            // Collect from Configuration NC
            var producerTaskNC = _producer.ProduceConfigNC();
            await producerTaskNC;

            _log.LogInformation("Producer has finished, closing LDAP channel");
            _ldapChannel.Writer.Complete();
            _log.LogInformation("LDAP channel closed, waiting for consumers");
            await Task.WhenAll(_taskPool);
            _log.LogInformation("Consumers finished, closing output channel");

            await foreach (var wkp in _context.LDAPUtils.GetWellKnownPrincipalOutput())
            {
                if (!wkp.ObjectIdentifier.EndsWith(EnterpriseDCSuffix))
                {
                    wkp.Properties["reconcile"] = false;
                }
                else if (wkp is Group g && g.Members.Length == 0)
                {
                    continue;
                }

                await _outputChannel.Writer.WriteAsync(wkp);
            }
                

            _outputChannel.Writer.Complete();
            _compStatusChannel?.Writer.Complete();
            _log.LogInformation("Output channel closed, waiting for output task to complete");
            var zipFile = await outputTask;
            if (compStatusTask != null) await compStatusTask;

            return zipFile;
        }

        internal async Task ConsumeSearchResults()
        {
            var log = _context.Logger;
            var processor = new ObjectProcessors(_context, log);
            var watch = new Stopwatch();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            
            await foreach (var item in _ldapChannel.Reader.ReadAllAsync())
                try
                {
                    if (await LdapUtils.ResolveSearchResult(item, _context.LDAPUtils) is not (true, var res) || res == null || res.ObjectType == Label.Base)
                    {
                        if (item.TryGetDistinguishedName(out var dn))
                        {
                            log.LogTrace("Consumer failed to resolve entry for {item} or label was Base", dn);
                        }
                        continue;
                    }

                    log.LogTrace("Consumer {ThreadID} started processing {obj} ({type})", threadId, res.DisplayName, res.ObjectType);
                    watch.Start();
                    var processed = await processor.ProcessObject(item, res, _compStatusChannel);
                    watch.Stop();
                    log.LogTrace("Consumer {ThreadID} took {time} ms to process {obj}", threadId,
                        watch.Elapsed.TotalMilliseconds, res.DisplayName);
                    if (processed == null)
                        continue;

                    if (processed is Domain d && _context.CollectedDomainSids.Contains(d.ObjectIdentifier))
                    {
                        d.Properties.Add("collected", true);
                    }
                    await _outputChannel.Writer.WriteAsync(processed);
                }
                catch (Exception e)
                {
                    log.LogError(e, "error in consumer");
                }

            log.LogDebug("Consumer task on thread {id} completed", Thread.CurrentThread.ManagedThreadId);
        }
    }
}