﻿using System.Diagnostics;
using System.IO;
using System.Linq;
using CloudStructures.Converters;
using CloudStructures.Internals;
using StackExchange.Redis;



namespace CloudStructures
{
    /// <summary>
    /// Provides connection to the server.
    /// </summary>
    /// <remarks>This connection needs to be used w/o destroying. Please hold as static field or static property.</remarks>
    public sealed class RedisConnection : System.IDisposable
    {
        #region Properties
        /// <summary>
        /// Gets configuration.
        /// </summary>
        public RedisConfig Config { get; }


        /// <summary>
        /// Gets value converter.
        /// </summary>
        internal ValueConverter Converter { get; }


        /// <summary>
        /// Gets connection event handler.
        /// </summary>
        private IConnectionEventHandler? Handler { get; }


        /// <summary>
        /// Gets logger.
        /// </summary>
        private TextWriter? Logger { get; }


        /// <summary>
        /// Gets an interactive connection to a database inside redis.
        /// </summary>
        internal IDatabaseAsync Database
            => this.Config.Database.HasValue
            ? this.GetConnection().GetDatabase(this.Config.Database.Value)
            : this.GetConnection().GetDatabase();


        /// <summary>
        /// Gets a transaction.
        /// </summary>
        internal ITransaction Transaction
            => ((IDatabase)this.Database).CreateTransaction();


        /// <summary>
        /// Gets target servers.
        /// </summary>
        internal IServer[] Servers
            => this.Config.Options
            .EndPoints
            .Select(this.GetConnection(), (x, c) => c.GetServer(x))
            .ToArray();
        #endregion


        #region Constructors
        /// <summary>
        /// Creates instance.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="converter">If null, use <see cref="SystemTextJsonConverter"/> as default.</param>
        /// <param name="handler"></param>
        /// <param name="logger"></param>
        public RedisConnection(RedisConfig config, IValueConverter? converter = null, IConnectionEventHandler? handler = null, TextWriter? logger = null)
        {
            this.Config = config;
            this.Converter = new(converter);
            this.Handler = handler;
            this.Logger = logger;
        }
        #endregion


        #region Connection management
        /// <summary>
        /// Gets underlying connection.
        /// </summary>
        /// <returns></returns>
        public ConnectionMultiplexer GetConnection()
        {
            lock (this.gate)
            {
                if (this.connection == null)
                {
                    try
                    {
                        //--- create inner connection
                        var stopwatch = Stopwatch.StartNew();
                        this.connection = ConnectionMultiplexer.Connect(this.Config.Options, this.Logger);
                        stopwatch.Stop();
                        this.Handler?.OnConnectionOpened(this, new(stopwatch.Elapsed));

                        //--- attach events
                        this.connection.ConfigurationChanged += (_, e) => this.Handler?.OnConfigurationChanged(this, e);
                        this.connection.ConfigurationChangedBroadcast += (_, e) => this.Handler?.OnConfigurationChangedBroadcast(this, e);
                        this.connection.ConnectionFailed += (_, e) => this.Handler?.OnConnectionFailed(this, e);
                        this.connection.ConnectionRestored += (_, e) => this.Handler?.OnConnectionRestored(this, e);
                        this.connection.ErrorMessage += (_, e) => this.Handler?.OnErrorMessage(this, e);
                        this.connection.HashSlotMoved += (_, e) => this.Handler?.OnHashSlotMoved(this, e);
                        this.connection.InternalError += (_, e) => this.Handler?.OnInternalError(this, e);
                    }
                    catch
                    {
                        this.connection = null;
                        throw;
                    }
                }
                return this.connection;
            }
        }

        public void Dispose()
        {
            if (connection != null)
            {
                connection.Dispose();
                connection = null;
            }
        }

        private readonly object gate = new();
        private ConnectionMultiplexer? connection = null;
        #endregion
    }
}
