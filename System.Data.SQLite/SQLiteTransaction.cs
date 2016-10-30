/********************************************************
 * ADO.NET 2.0 Data Provider for SQLite Version 3.X
 * Written by Robert Simpson (robert@blackcastlesoft.com)
 *
 * Released to the public domain, use at your own risk!
 ********************************************************/

namespace System.Data.SQLite
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Threading;

    ///////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// SQLite implementation of DbTransaction.
    /// </summary>
    public sealed class SQLiteTransaction : DbTransaction
    {
        /// <summary>
        /// The connection to which this transaction is bound
        /// </summary>
        internal SQLiteConnection _cnn;

        /// <summary>
        /// Matches the version of the connection.
        /// </summary>
        private int _version;

        /// <summary>
        /// The isolation level for this transaction.
        /// </summary>
        private IsolationLevel _level;

        /// <summary>
        /// The original transaction level for the associated connection
        /// when this transaction was created (i.e. begun).
        /// </summary>
        private int _beginLevel;

        /// <summary>
        /// The SAVEPOINT names for each transaction level.
        /// </summary>
        private Dictionary<int, string> _savePointNames;

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Constructs the transaction object, binding it to the supplied connection
        /// </summary>
        /// <param name="connection">The connection to open a transaction on</param>
        /// <param name="deferredLock">TRUE to defer the writelock, or FALSE to lock immediately</param>
        internal SQLiteTransaction(SQLiteConnection connection, bool deferredLock)
        {
            _cnn = connection;
            _version = _cnn._version;

            _level = (deferredLock == true) ?
                SQLiteConnection.DeferredIsolationLevel :
                SQLiteConnection.ImmediateIsolationLevel;

            int transactionLevel;

            if ((transactionLevel = _cnn._transactionLevel++) == 0)
            {
                try
                {
                    using (SQLiteCommand cmd = _cnn.CreateCommand())
                    {
                        if (!deferredLock)
                            cmd.CommandText = "BEGIN IMMEDIATE;";
                        else
                            cmd.CommandText = "BEGIN;";

                        cmd.ExecuteNonQuery();

                        _beginLevel = transactionLevel;
                    }
                }
                catch (SQLiteException)
                {
                    _cnn._transactionLevel--;
                    _cnn = null;

                    throw;
                }
            }
            else
            {
                try
                {
                    using (SQLiteCommand cmd = _cnn.CreateCommand())
                    {
                        cmd.CommandText = String.Format(
                            "SAVEPOINT {0};", GetSavePointName(
                            transactionLevel));

                        cmd.ExecuteNonQuery();

                        _beginLevel = transactionLevel;
                    }
                }
                catch (SQLiteException)
                {
                    _cnn._transactionLevel--;
                    _cnn = null;

                    throw;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        #region IDisposable "Pattern" Members
        private bool disposed;
        private void CheckDisposed() /* throw */
        {
#if THROW_ON_DISPOSED
            if (disposed)
                throw new ObjectDisposedException(typeof(SQLiteTransaction).Name);
#endif
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Disposes the transaction.  If it is currently active, any changes are rolled back.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        ////////////////////////////////////
                        // dispose managed resources here...
                        ////////////////////////////////////

                        if (IsValid(false))
                        {
                            IssueRollback(false);
                        }
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////
                }
            }
            finally
            {
                base.Dispose(disposing);

                //
                // NOTE: Everything should be fully disposed at this point.
                //
                disposed = true;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Commits the current transaction.
        /// </summary>
        public override void Commit()
        {
            CheckDisposed();
            SQLiteConnection.Check(_cnn);
            IsValid(true);

            if (_beginLevel == 0)
            {
                using (SQLiteCommand cmd = _cnn.CreateCommand())
                {
                    cmd.CommandText = "COMMIT;";
                    cmd.ExecuteNonQuery();
                }

                _cnn._transactionLevel = 0;
                _cnn = null;
            }
            else
            {
                using (SQLiteCommand cmd = _cnn.CreateCommand())
                {
                    int transactionLevel = _cnn._transactionLevel;

                    cmd.CommandText = String.Format(
                        "RELEASE {0};", GetSavePointName(
                        transactionLevel - 1));

                    cmd.ExecuteNonQuery();
                }

                _cnn._transactionLevel--;
                _cnn = null;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Returns the underlying connection to which this transaction applies.
        /// </summary>
        public new SQLiteConnection Connection
        {
            get { CheckDisposed(); return _cnn; }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Forwards to the local Connection property
        /// </summary>
        protected override DbConnection DbConnection
        {
            get { return Connection; }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Gets the isolation level of the transaction.  SQLite only supports Serializable transactions.
        /// </summary>
        public override IsolationLevel IsolationLevel
        {
            get { CheckDisposed(); return _level; }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Rolls back the active transaction.
        /// </summary>
        public override void Rollback()
        {
            CheckDisposed();
            SQLiteConnection.Check(_cnn);
            IsValid(true);
            IssueRollback(true);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Issue a ROLLBACK command against the database connection,
        /// optionally re-throwing any caught exception.
        /// </summary>
        /// <param name="throwError">
        /// Non-zero to re-throw caught exceptions.
        /// </param>
        private void IssueRollback(bool throwError)
        {
            SQLiteConnection cnn = Interlocked.Exchange(ref _cnn, null);

            if (cnn != null)
            {
                if (_beginLevel == 0)
                {
                    try
                    {
                        using (SQLiteCommand cmd = cnn.CreateCommand())
                        {
                            cmd.CommandText = "ROLLBACK;";
                            cmd.ExecuteNonQuery();
                        }

                        cnn._transactionLevel = 0;
                    }
                    catch
                    {
                        if (throwError)
                            throw;
                    }
                }
                else
                {
                    try
                    {
                        using (SQLiteCommand cmd = cnn.CreateCommand())
                        {
                            int transactionLevel = cnn._transactionLevel;

                            cmd.CommandText = String.Format(
                                "ROLLBACK TO {0};", GetSavePointName(
                                transactionLevel - 1));

                            cmd.ExecuteNonQuery();
                        }

                        cnn._transactionLevel--;
                    }
                    catch
                    {
                        if (throwError)
                            throw;
                    }
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Constructs the name of a new or existing savepoint.
        /// </summary>
        /// <param name="transactionLevel">
        /// The transaction level associated with the connection.
        /// </param>
        /// <returns>
        /// The name of the savepoint -OR- null if it cannot be constructed.
        /// </returns>
        private string GetSavePointName(
            int transactionLevel
            )
        {
            if (_savePointNames == null)
                _savePointNames = new Dictionary<int, string>();

            string name;

            if (!_savePointNames.TryGetValue(transactionLevel, out name))
            {
                int sequence = ++_cnn._transactionSequence;

                name = String.Format(
                    "sqlite_dotnet_savepoint_{0}_{1}",
                    transactionLevel, sequence);

                _savePointNames[transactionLevel] = name;
            }

            return name;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Checks the state of this transaction, optionally throwing an exception if a state inconsistency is found.
        /// </summary>
        /// <param name="throwError">
        /// Non-zero to throw an exception if a state inconsistency is found.
        /// </param>
        /// <returns>
        /// Non-zero if this transaction is valid; otherwise, false.
        /// </returns>
        internal bool IsValid(bool throwError)
        {
            if (_cnn == null)
            {
                if (throwError == true) throw new ArgumentNullException("No connection associated with this transaction");
                else return false;
            }

            if (_cnn._version != _version)
            {
                if (throwError == true) throw new SQLiteException("The connection was closed and re-opened, changes were already rolled back");
                else return false;
            }
            if (_cnn.State != ConnectionState.Open)
            {
                if (throwError == true) throw new SQLiteException("Connection was closed");
                else return false;
            }

            if (_cnn._transactionLevel == 0 || _cnn._sql.AutoCommit == true)
            {
                _cnn._transactionLevel = 0; // Make sure the transaction level is reset before returning
                if (throwError == true) throw new SQLiteException("No transaction is active on this connection");
                else return false;
            }

            return true;
        }
    }
}
