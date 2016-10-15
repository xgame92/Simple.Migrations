﻿using System;
using System.Data;

namespace SimpleMigrations
{
    /// <summary>
    /// Version provider which acts by maintaining a table of applied versions
    /// </summary>
    public abstract class VersionProviderBase : IVersionProvider<IDbConnection>
    {
        /// <summary>
        /// Default table name used to store version info
        /// </summary>
        public const string DefaultTableName = "VersionInfo";

        private IDbConnection connection;

        /// <summary>
        /// Gets or sets a value indicating whether to run <see cref="EnsureCreated"/>
        /// or <see cref="GetCurrentVersion"/> in a transaction
        /// </summary>
        protected bool UseTransaction { get; set; }

        /// <summary>
        /// Sets the connection to use. Must be set before calling other methods
        /// </summary>
        public void SetConnection(IDbConnection connection)
        {
            this.connection = connection;
        }

        /// <summary>
        /// Ensures that the class has been properly set up
        /// </summary>
        protected void EnsureSetup()
        {
            if (this.connection == null)
                throw new InvalidOperationException("this.Connection must be assigned before calling this method");
        }

        /// <summary>
        /// Ensure that the version table exists, creating it if necessary
        /// </summary>
        public virtual void EnsureCreated()
        {
            this.EnsureSetup();

            this.RunInTransactionIfConfigured(command =>
            {
                command.CommandText = this.GetCreateVersionTableSql();
                command.ExecuteNonQuery();
            });
        }

        /// <summary>
        /// Return the current version from the version table
        /// </summary>
        /// <returns>Current version</returns>
        public virtual long GetCurrentVersion()
        {
            this.EnsureSetup();

            long version = 0;
            this.RunInTransactionIfConfigured(command =>
            {
                command.CommandText = this.GetCurrentVersionSql();
                var result = command.ExecuteScalar();

                if (result == null)
                    return;

                try
                {
                    version = Convert.ToInt64(result);
                }
                catch
                {
                    throw new MigrationException("Version Provider returns a value for the current version which isn't a long");
                }
            });

            return version;
        }

        /// <summary>
        /// Upgrade the current version in the version table
        /// </summary>
        /// <param name="oldVersion">Version being upgraded from</param>
        /// <param name="newVersion">Version being upgraded to</param>
        /// <param name="newDescription">Description to associate with the new version</param>
        public virtual void UpdateVersion(long oldVersion, long newVersion, string newDescription)
        {
            this.EnsureSetup();

            using (var command = this.connection.CreateCommand())
            {
                command.CommandText = this.GetSetVersionSql();

                var versionParam = command.CreateParameter();
                versionParam.ParameterName = "Version";
                versionParam.Value = newVersion;
                command.Parameters.Add(versionParam);

                var nameParam = command.CreateParameter();
                nameParam.ParameterName = "Description";
                nameParam.Value = newDescription;
                command.Parameters.Add(nameParam);

                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Runs the given action in a transaction
        /// </summary>
        /// <param name="action">Action to be executed. This takes a command, which already has the transaction assigned</param>
        protected virtual void RunInTransactionIfConfigured(Action<IDbCommand> action)
        {
            this.EnsureSetup();

            if (this.UseTransaction)
            {
                using (var transaction = this.connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        using (var command = this.connection.CreateCommand())
                        {
                            action(command);
                        }
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            else
            {
                using (var command = this.connection.CreateCommand())
                {
                    action(command);
                }
            }
        }

        /// <summary>
        /// Should 'CREATE TABLE IF NOT EXISTS', or similar
        /// </summary>
        public abstract string GetCreateVersionTableSql();

        /// <summary>
        /// Should return SQL which selects a single long value - the current version - or 0/NULL if there is no current version
        /// </summary>
        /// <returns></returns>
        public abstract string GetCurrentVersionSql();

        /// <summary>
        /// Returns SQL which upgrades to a particular version.
        /// </summary>
        /// <remarks>
        /// The following parameters should be used:
        ///  - @Version - the long version to set
        ///  - @Description - the description of the version
        /// </remarks>
        /// <returns></returns>
        public abstract string GetSetVersionSql();
    }
}
