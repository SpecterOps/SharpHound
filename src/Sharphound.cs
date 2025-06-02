// ---------------------------------------------------- //
//    ______                 __ __                  __  //
//   / __/ /  ___ ________  / // /_   __ _____  ___/ /  //
//  _\ \/ _ \/ _ `/ __/ _ \/ _  / _ \/ // / _ \/ _  /   //
// /___/_//_/\_,_/_/ / .__/_//_/\___/\_,_/_//_/\_,_/    //
//                  /_/                                 //
//  app type    : console                               //
//  dotnet ver. : 462                                   //
//  client ver  : 3?                                    //
//  license     : open....?                             //
//------------------------------------------------------//
// creational_pattern : Inherit from System.CommandLine //
// structural_pattern  : Chain Of Responsibility         //
// behavioral_pattern : inherit from SharpHound3        //
// ---------------------------------------------------- //

using System;
using System.DirectoryServices.Protocols;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Sharphound.Client;
using SharpHoundCommonLib;
using SharpHoundCommonLib.Enums;

namespace Sharphound
{

    #region Reference Implementations

    #endregion

    #region Console Entrypoint

    public class Program {
        public static async Task Main(string[] args) {
            var logger = new BasicLogger((int)LogLevel.Information);
            logger.LogInformation("This version of SharpHound is compatible with the 5.0.0 Release of BloodHound");

            try {
                // Checks the release version available on the machine.
                var releaseVersion = (int) Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", "Release", 0);
                if (releaseVersion == 0) releaseVersion = (int) Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", "Release", 0);
                // The value 461808 corresponds to .Net 4.7.2
                if (releaseVersion < 461808)
                {
                    logger.LogError("The .Net Runtime is not compatible with SharpHound. Please update to .Net 4.7.2.");
                    return;
                }

                var parser = new Parser(with => {
                    with.CaseInsensitiveEnumValues = true;
                    with.CaseSensitive = false;
                    with.HelpWriter = Console.Error;
                });
                var options = parser.ParseArguments<Options>(args);

                await options.WithParsedAsync(async options =>
                {
                    if (!options.ResolveCollectionMethods(logger, out var resolved, out var dconly)) return;

                    logger = new BasicLogger(options.Verbosity);

                    var flags = new Flags
                    {
                        Loop = options.Loop,
                        DumpComputerStatus = options.TrackComputerCalls,
                        NoRegistryLoggedOn = options.SkipRegistryLoggedOn,
                        ExcludeDomainControllers = options.ExcludeDCs,
                        SkipPortScan = options.SkipPortCheck,
                        SkipPasswordAgeCheck = options.SkipPasswordCheck,
                        DisableKerberosSigning = options.DisableSigning,
                        SecureLDAP = options.ForceSecureLDAP,
                        InvalidateCache = options.RebuildCache,
                        NoZip = options.NoZip,
                        NoOutput = false,
                        Stealth = options.Stealth,
                        RandomizeFilenames = options.RandomFileNames,
                        MemCache = options.MemCache,
                        CollectAllProperties = options.CollectAllProperties,
                        DCOnly = dconly,
                        PrettyPrint = options.PrettyPrint,
                        SearchForest = options.SearchForest,
                        RecurseDomains = options.RecurseDomains,
                        DoLocalAdminSessionEnum = options.DoLocalAdminSessionEnum,
                        ParititonLdapQueries = options.PartitionLdapQueries
                    };

                    var ldapOptions = new LdapConfig
                    {
                        Port = options.LDAPPort,
                        SSLPort = options.LDAPSSLPort,
                        DisableSigning = options.DisableSigning,
                        ForceSSL = options.ForceSecureLDAP,
                        AuthType = AuthType.Negotiate,
                        DisableCertVerification = options.DisableCertVerification
                    };

                    if (options.DomainController != null) ldapOptions.Server = options.DomainController;

                    if (options.LDAPUsername != null)
                    {
                        if (options.LDAPPassword == null)
                        {
                            logger.LogError("You must specify LDAPPassword if using the LDAPUsername options");
                            return;
                        }

                        ldapOptions.Username = options.LDAPUsername;
                        ldapOptions.Password = options.LDAPPassword;
                    }

                    // Check to make sure both Local Admin Session Enum options are set if either is set

                    if (options.LocalAdminPassword != null && options.LocalAdminUsername == null ||
                        options.LocalAdminUsername != null && options.LocalAdminPassword == null)
                    {
                        logger.LogError(
                            "You must specify both LocalAdminUsername and LocalAdminPassword if using these options!");
                        return;
                    }

                    // Check to make sure doLocalAdminSessionEnum is set when specifying localadmin and password

                    if (options.LocalAdminPassword != null || options.LocalAdminUsername != null)
                    {
                        if (options.DoLocalAdminSessionEnum == false)
                        {
                            logger.LogError(
                                "You must use the --doLocalAdminSessionEnum switch in combination with --LocalAdminUsername and --LocalAdminPassword!");
                            return;
                        }
                    }

                    // Check to make sure LocalAdminUsername and LocalAdminPassword are set when using doLocalAdminSessionEnum

                    if (options.DoLocalAdminSessionEnum == true)
                    {
                        if (options.LocalAdminPassword == null || options.LocalAdminUsername == null)
                        {
                            logger.LogError(
                                "You must specify both LocalAdminUsername and LocalAdminPassword if using the --doLocalAdminSessionEnum option!");
                            return;
                        }
                    }

                    await StartCollection(options, logger, resolved, flags, ldapOptions);
                });
            } catch (Exception ex) {
                logger.LogError($"Error running SharpHound: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static async Task StartCollection(Options options, BasicLogger logger, CollectionMethod resolved, Flags flags, LdapConfig ldapOptions)
        {
            IContext context = new BaseContext(logger, ldapOptions, flags)
            {
                DomainName = options.Domain,
                CacheFileName = options.CacheName,
                ZipFilename = options.ZipFilename,
                SearchBase = options.DistinguishedName,
                StatusInterval = options.StatusInterval,
                RealDNSName = options.RealDNSName,
                ComputerFile = options.ComputerFile,
                OutputPrefix = options.OutputPrefix,
                OutputDirectory = options.OutputDirectory,
                Jitter = options.Jitter,
                Throttle = options.Throttle,
                LdapFilter = options.LdapFilter,
                PortScanTimeout = options.PortCheckTimeout,
                ResolvedCollectionMethods = resolved,
                Threads = options.Threads,
                LoopDuration = options.LoopDuration,
                LoopInterval = options.LoopInterval,
                ZipPassword = options.ZipPassword,
                IsFaulted = false,
                LocalAdminUsername = options.LocalAdminUsername,
                LocalAdminPassword = options.LocalAdminPassword
            };

            var cancellationTokenSource = new CancellationTokenSource();
            context.CancellationTokenSource = cancellationTokenSource;

            // Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
            // {
            //     eventArgs.Cancel = true;
            //     cancellationTokenSource.Cancel();
            // };

            // Create new chain links
            Links<IContext> links = new SharpLinks();

            // Run our chain
            context = links.Initialize(context, ldapOptions);
            if (context.Flags.IsFaulted)
                return;
            context = await links.TestConnection(context);
            if (context.Flags.IsFaulted)
                return;
            context = links.SetSessionUserName(options.OverrideUserName, context);
            context = links.InitCommonLib(context);
            context = await links.GetDomainsForEnumeration(context);
            if (context.Flags.IsFaulted)
                return;
            context = links.StartBaseCollectionTask(context);
            context = await links.AwaitBaseRunCompletion(context);
            context = links.StartLoopTimer(context);
            context = links.StartLoop(context);
            context = await links.AwaitLoopCompletion(context);
            context = links.SaveCacheFile(context);
            links.Finish(context);
        }

        // Accessor function for the PS1 to work, do not change or remove
        public static void InvokeSharpHound(string[] args) {
            Main(args).Wait();
        }
    }

    #endregion
}