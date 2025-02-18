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
using Microsoft.Extensions.Logging;

namespace Sharphound
{
    #region Reference Implementations

    internal class BasicLogger : ILogger {
        private readonly int _verbosity;

        public BasicLogger(int verbosity) {
            _verbosity = verbosity;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter) {
            WriteLevel(logLevel, state.ToString(), exception);
        }

        public bool IsEnabled(LogLevel logLevel) {
            return (int)logLevel >= _verbosity;
        }

        public IDisposable BeginScope<TState>(TState state) {
            return null;
        }

        private void WriteLevel(LogLevel level, string message, Exception e = null) {
            if (IsEnabled(level))
                Console.WriteLine(FormatLog(level, message, e));
        }

        private static string FormatLog(LogLevel level, string message, Exception e) {
            var time = DateTime.Now;
            return $"{time:O}|{level.ToString().ToUpper()}|{message}{(e != null ? $"\n{e}" : "")}";
        }
    }

    #endregion
}