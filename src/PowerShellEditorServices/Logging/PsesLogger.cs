using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PowerShellEditorServices.Logging
{
    internal static class PsesLoggerFactoryExtensions
    {
        public static ILoggerFactory AddPsesLogger(this ILoggerFactory factory, string logPath)
        {
            factory.AddProvider(PsesLoggerProvider.FromFile(logPath));
            return factory;
        }
    }

    internal interface ILogSink : IDisposable
    {
        void Log(LogLevel logLevel, string logMessage);
    }

    internal class AsyncFileLogSink : ILogSink
    {
        private readonly FileStream _fileStream;

        private readonly StreamWriter _streamWriter;

        private readonly LogLevel _minimumLogLevel;

        private readonly BlockingCollection<string> _logQueue;

        private readonly CancellationTokenSource _cancellationSource;

        private readonly Thread _writerThread;

        private int _stopped = 0;

        public AsyncFileLogSink(string filePath, LogLevel minimumLogLevel)
        {
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
            _streamWriter = new StreamWriter(_fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
            {
                AutoFlush = true,
            };
            _minimumLogLevel = minimumLogLevel;
            _logQueue = new BlockingCollection<string>();
            _cancellationSource = new CancellationTokenSource();
            _writerThread = new Thread(RunLogListener);
        }

        public void Log(LogLevel logLevel, string logMessage)
        {
            if (logLevel < _minimumLogLevel)
            {
                return;
            }

            _logQueue.Add(logMessage);
        }

        public void Dispose()
        {
            StopLogListener();
            _streamWriter.Dispose();
            _fileStream.Dispose();
        }

        private void RunLogListener()
        {
            try
            {
                foreach (string logMessage in _logQueue.GetConsumingEnumerable(_cancellationSource.Token))
                {
                    _streamWriter.WriteLine(logMessage);
                }
            }
            catch (OperationCanceledException)
            {
                _logQueue.CompleteAdding();
            }
        }

        private void StopLogListener()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            _cancellationSource.Cancel();
            _writerThread.Join();
            _streamWriter.Flush();
            _streamWriter.Close();
        }
    }

    internal class PsesLoggerProvider : ILoggerProvider
    {
        private readonly List<ILogSink> _sinks;

        private PsesLoggerProvider()
        {
            _sinks = new List<ILogSink>();
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new PsesLogger(categoryName, _sinks);
        }

        public void Dispose()
        {
            foreach (ILogSink sink in _sinks)
            {
                sink.Dispose();
            }
        }
    }

    internal class PsesLogger : ILogger
    {
        private readonly string _categoryName;

        private readonly IReadOnlyCollection<ILogSink> _sinks;

        private readonly LogLevel _minimumLogLevel;

        public PsesLogger(string categoryName, IReadOnlyCollection<ILogSink> sinks, LogLevel minimumLogLevel)
        {
            _categoryName = categoryName;
            _sinks = sinks;
            _minimumLogLevel = minimumLogLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minimumLogLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            throw new NotImplementedException();
        }
    }
}
