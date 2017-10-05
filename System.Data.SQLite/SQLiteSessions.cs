/********************************************************
 * ADO.NET 2.0 Data Provider for SQLite Version 3.X
 * Written by Joe Mistachkin (joe@mistachkin.com)
 *
 * Released to the public domain, use at your own risk!
 ********************************************************/

using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;

namespace System.Data.SQLite
{
    #region Session Extension Enumerations
    public enum SQLiteChangeSetConflictType
    {
        Data = 1,
        NotFound = 2,
        Conflict = 3,
        Constraint = 4,
        ForeignKey = 5
    }

    ///////////////////////////////////////////////////////////////////////////

    public enum SQLiteChangeSetConflictResult
    {
        Omit = 0,
        Replace = 1,
        Abort = 2
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region Session Extension Delegates
    public delegate bool TableFilterDelegate(
        object context,
        string name
    );

    ///////////////////////////////////////////////////////////////////////////

    public delegate SQLiteChangeSetConflictResult ConflictDelegate(
        object context,
        SQLiteChangeSetConflictType type,
        ISQLiteChangeSetMetadataItem item
    );
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region Session Extension Interfaces
    public interface ISQLiteChangeSet
    {
        bool? IsPatchSet { get; }

        void Apply(SQLiteConnection connection, ConflictDelegate conflictCallback);
        void Apply(SQLiteConnection connection, ConflictDelegate conflictCallback, TableFilterDelegate tableFilterCallback);

        ISQLiteChangeSet Invert();

        ISQLiteChangeSet CombineWith(ISQLiteChangeSet changeSet);
    }

    ///////////////////////////////////////////////////////////////////////////

    public interface ISQLiteChangeGroup
    {
        void AddChangeSet(byte[] rawData);
        void AddChangeSet(Stream stream);

        void CreateChangeSet(ref byte[] rawData);
        void CreateChangeSet(Stream stream, SQLiteConnectionFlags flags);
    }

    ///////////////////////////////////////////////////////////////////////////

    public interface ISQLiteChangeSetMetadataItem
    {
        SQLiteAuthorizerActionCode OperationCode { get; }
        string TableName { get; }
        int NumberOfColumns { get; }
        bool Indirect { get; }
        bool[] PrimaryKeyColumns { get; }

        SQLiteValue GetOldValue(int columnIndex);
        SQLiteValue GetNewValue(int columnIndex);

        SQLiteValue GetConflictValue(int columnIndex);
        int NumberOfForeignKeyConflicts { get; }
    }

    ///////////////////////////////////////////////////////////////////////////

    public interface ISQLiteSession
    {
        bool IsEnabled();
        void SetToEnabled();
        void SetToDisabled();

        bool IsIndirect();
        void SetToIndirect();
        void SetToDirect();

        bool IsEmpty();

        void AttachTable(string name);
        void SetTableFilter(TableFilterDelegate callback, object context);

        void CreateChangeSet(ref byte[] rawData);
        void CreateChangeSet(Stream stream, SQLiteConnectionFlags flags);

        void CreatePatchSet(ref byte[] rawData);
        void CreatePatchSet(Stream stream, SQLiteConnectionFlags flags);

        void LoadDifferencesFromTable(string fromDatabaseName, string tableName);
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeSetIterator Class
    internal sealed class SQLiteChangeSetIterator : IDisposable
    {
        #region Private Data
        private IntPtr pData;
        private IntPtr iterator;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        private SQLiteChangeSetIterator(
            IntPtr pData,
            IntPtr iterator
            )
        {
            this.pData = pData;
            this.iterator = iterator;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Static "Factory" Methods
        public static SQLiteChangeSetIterator Create(
            byte[] rawData
            )
        {
            if (rawData == null)
                throw new ArgumentNullException("rawData");

            SQLiteChangeSetIterator result = null;
            IntPtr pData = IntPtr.Zero;
            IntPtr iterator = IntPtr.Zero;

            try
            {
                int nData = 0;

                pData = SQLiteBytes.ToIntPtr(rawData, ref nData);

                if (pData == IntPtr.Zero)
                    throw new SQLiteException(SQLiteErrorCode.NoMem, null);

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_start(
                    ref iterator, nData, pData);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changeset_start");

                result = new SQLiteChangeSetIterator(pData, iterator);
            }
            finally
            {
                if (result == null)
                {
                    if (iterator != IntPtr.Zero)
                    {
                        UnsafeNativeMethods.sqlite3changeset_finalize(
                            iterator);

                        iterator = IntPtr.Zero;
                    }

                    if (pData != IntPtr.Zero)
                    {
                        SQLiteMemory.Free(pData);
                        pData = IntPtr.Zero;
                    }
                }
            }

            return result;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable Members
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable "Pattern" Members
        private bool disposed;
        private void CheckDisposed() /* throw */
        {
#if THROW_ON_DISPOSED
            if (disposed)
            {
                throw new ObjectDisposedException(
                    typeof(SQLiteChangeSetIterator).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        private /* protected virtual */ void Dispose(bool disposing)
        {
            try
            {
                if (!disposed)
                {
                    //if (disposing)
                    //{
                    //    ////////////////////////////////////
                    //    // dispose managed resources here...
                    //    ////////////////////////////////////
                    //}

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////

                    if (iterator != IntPtr.Zero)
                    {
                        UnsafeNativeMethods.sqlite3changeset_finalize(
                            iterator);

                        iterator = IntPtr.Zero;
                    }

                    if (pData != IntPtr.Zero)
                    {
                        SQLiteMemory.Free(pData);
                        pData = IntPtr.Zero;
                    }
                }
            }
            finally
            {
                //
                // NOTE: Everything should be fully disposed at this point.
                //
                disposed = true;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Destructor
        ~SQLiteChangeSetIterator()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteStreamAdapter Class
    internal sealed class SQLiteStreamAdapter : IDisposable
    {
        #region Private Data
        private Stream stream;
        private SQLiteConnectionFlags flags;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteStreamAdapter(
            Stream stream,
            SQLiteConnectionFlags flags
            )
        {
            this.stream = stream;
            this.flags = flags;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Native Callback Methods
        public SQLiteErrorCode xInput(
            IntPtr context,
            IntPtr pData,
            ref int nData
            )
        {
            try
            {
                if (stream == null)
                    return SQLiteErrorCode.Misuse;

                if (nData > 0)
                {
                    byte[] bytes = new byte[nData];

                    nData = stream.Read(bytes, 0, nData);
                    Marshal.Copy(bytes, 0, pData, nData);
                }

                return SQLiteErrorCode.Ok;
            }
            catch (Exception e)
            {
                try
                {
                    if ((flags & SQLiteConnectionFlags.LogCallbackException) ==
                            SQLiteConnectionFlags.LogCallbackException)
                    {
                        SQLiteLog.LogMessage(SQLiteBase.COR_E_EXCEPTION,
                            HelperMethods.StringFormat(CultureInfo.CurrentCulture,
                            "Caught exception in \"xInput\" method: {0}",
                            e)); /* throw */
                    }
                }
                catch
                {
                    // do nothing.
                }
            }

            return SQLiteErrorCode.IoErr_Read;
        }

        ///////////////////////////////////////////////////////////////////////

        public SQLiteErrorCode xOutput(
            IntPtr context,
            IntPtr pData,
            int nData
            )
        {
            try
            {
                if (stream == null)
                    return SQLiteErrorCode.Misuse;

                if (nData > 0)
                {
                    byte[] bytes = new byte[nData];

                    Marshal.Copy(pData, bytes, 0, nData);
                    stream.Write(bytes, 0, nData);
                }

                stream.Flush();

                return SQLiteErrorCode.Ok;
            }
            catch (Exception e)
            {
                try
                {
                    if ((flags & SQLiteConnectionFlags.LogCallbackException) ==
                            SQLiteConnectionFlags.LogCallbackException)
                    {
                        SQLiteLog.LogMessage(SQLiteBase.COR_E_EXCEPTION,
                            HelperMethods.StringFormat(CultureInfo.CurrentCulture,
                            "Caught exception in \"xOutput\" method: {0}",
                            e)); /* throw */
                    }
                }
                catch
                {
                    // do nothing.
                }
            }

            return SQLiteErrorCode.IoErr_Write;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable Members
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable "Pattern" Members
        private bool disposed;
        private void CheckDisposed() /* throw */
        {
#if THROW_ON_DISPOSED
            if (disposed)
            {
                throw new ObjectDisposedException(
                    typeof(SQLiteStreamAdapter).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        private /* protected virtual */ void Dispose(bool disposing)
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

                        if (stream != null)
                            stream = null; /* NOT OWNED: Do not dispose. */
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////
                }
            }
            finally
            {
                //
                // NOTE: Everything should be fully disposed at this point.
                //
                disposed = true;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Destructor
        ~SQLiteStreamAdapter()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteSession Class
    public sealed class SQLiteSession : ISQLiteSession, IDisposable
    {
        #region Private Data
        private IntPtr session;

        ///////////////////////////////////////////////////////////////////////

        private TableFilterDelegate tableFilterCallback;
        private object tableFilterContext;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteSession(
            SQLiteConnection connection,
            string databaseName
            )
        {
            UnsafeNativeMethods.sqlite3session_create(
                SQLiteConnection.GetNativeHandle(connection),
                SQLiteString.GetUtf8BytesFromString(databaseName),
                ref session);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void CheckHandle()
        {
            if (session == IntPtr.Zero)
                throw new InvalidOperationException("session is not open");
        }

        ///////////////////////////////////////////////////////////////////////

        #region Native Callback Methods
        private int xFilter(
            IntPtr context, /* NOT USED */
            byte[] tblName
            )
        {
            return tableFilterCallback(tableFilterContext,
                SQLiteString.GetStringFromUtf8Bytes(tblName)) ? 1 : 0;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ISQLiteSession Members
        public bool IsEnabled()
        {
            CheckDisposed();
            CheckHandle();

            return UnsafeNativeMethods.sqlite3session_enable(session, -1) != 0;
        }

        ///////////////////////////////////////////////////////////////////////

        public void SetToEnabled()
        {
            CheckDisposed();
            CheckHandle();

            UnsafeNativeMethods.sqlite3session_enable(session, 1);
        }

        ///////////////////////////////////////////////////////////////////////

        public void SetToDisabled()
        {
            CheckDisposed();
            CheckHandle();

            UnsafeNativeMethods.sqlite3session_enable(session, 0);
        }

        ///////////////////////////////////////////////////////////////////////

        public bool IsIndirect()
        {
            CheckDisposed();
            CheckHandle();

            return UnsafeNativeMethods.sqlite3session_indirect(session, -1) != 0;
        }

        ///////////////////////////////////////////////////////////////////////

        public void SetToIndirect()
        {
            CheckDisposed();
            CheckHandle();

            UnsafeNativeMethods.sqlite3session_indirect(session, 1);
        }

        ///////////////////////////////////////////////////////////////////////

        public void SetToDirect()
        {
            CheckDisposed();
            CheckHandle();

            UnsafeNativeMethods.sqlite3session_indirect(session, 0);
        }

        ///////////////////////////////////////////////////////////////////////

        public bool IsEmpty()
        {
            CheckDisposed();
            CheckHandle();

            return UnsafeNativeMethods.sqlite3session_isempty(session) != 0;
        }

        ///////////////////////////////////////////////////////////////////////

        public void AttachTable(string name)
        {
            CheckDisposed();
            CheckHandle();

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_attach(
                session, SQLiteString.GetUtf8BytesFromString(name));

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3session_attach");
        }

        ///////////////////////////////////////////////////////////////////////

        public void SetTableFilter(
            TableFilterDelegate callback,
            object context
            )
        {
            CheckDisposed();
            CheckHandle();

            this.tableFilterCallback = callback;
            this.tableFilterContext = context;

            UnsafeNativeMethods.sqlite3session_table_filter(
                session, xFilter, IntPtr.Zero);
        }

        ///////////////////////////////////////////////////////////////////////

        public void CreateChangeSet(
            ref byte[] rawData
            )
        {
            CheckDisposed();
            CheckHandle();

            IntPtr pData = IntPtr.Zero;

            try
            {
                int nData = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_changeset(
                    session, ref nData, ref pData);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3session_changeset");

                rawData = SQLiteBytes.FromIntPtr(pData, nData);
            }
            finally
            {
                if (pData != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pData);
                    pData = IntPtr.Zero;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////

        public void CreateChangeSet(
            Stream stream,
            SQLiteConnectionFlags flags
            )
        {
            CheckDisposed();
            CheckHandle();

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_changeset_strm(
                session, new SQLiteStreamAdapter(stream, flags).xOutput,
                IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3session_changeset_strm");
        }

        ///////////////////////////////////////////////////////////////////////

        public void CreatePatchSet(
            ref byte[] rawData
            )
        {
            CheckDisposed();
            CheckHandle();

            IntPtr pData = IntPtr.Zero;

            try
            {
                int nData = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_patchset(
                    session, ref nData, ref pData);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3session_patchset");

                rawData = SQLiteBytes.FromIntPtr(pData, nData);
            }
            finally
            {
                if (pData != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pData);
                    pData = IntPtr.Zero;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////

        public void CreatePatchSet(
            Stream stream,
            SQLiteConnectionFlags flags
            )
        {
            CheckDisposed();
            CheckHandle();

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_patchset_strm(
                session, new SQLiteStreamAdapter(stream, flags).xOutput,
                IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3session_patchset_strm");
        }

        ///////////////////////////////////////////////////////////////////////

        public void LoadDifferencesFromTable(
            string fromDatabaseName,
            string tableName
            )
        {
            CheckDisposed();
            CheckHandle();

            IntPtr pError = IntPtr.Zero;

            try
            {
                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_diff(
                    session, SQLiteString.GetUtf8BytesFromString(fromDatabaseName),
                    SQLiteString.GetUtf8BytesFromString(tableName), ref pError);

                if (rc != SQLiteErrorCode.Ok)
                {
                    string error = null;

                    if (pError != IntPtr.Zero)
                    {
                        error = SQLiteString.StringFromUtf8IntPtr(pError);

                        if (!String.IsNullOrEmpty(error))
                        {
                            error = HelperMethods.StringFormat(
                                CultureInfo.CurrentCulture, ": {0}", error);
                        }
                    }

                    throw new SQLiteException(rc, HelperMethods.StringFormat(
                        CultureInfo.CurrentCulture, "{0}{1}",
                        "sqlite3session_diff", error));
                }
            }
            finally
            {
                if (pError != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pError);
                    pError = IntPtr.Zero;
                }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable Members
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable "Pattern" Members
        private bool disposed;
        private void CheckDisposed() /* throw */
        {
#if THROW_ON_DISPOSED
            if (disposed)
                throw new ObjectDisposedException(typeof(SQLiteSession).Name);
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        private /* protected virtual */ void Dispose(bool disposing)
        {
            try
            {
                if (!disposed)
                {
                    //if (disposing)
                    //{
                    //    ////////////////////////////////////
                    //    // dispose managed resources here...
                    //    ////////////////////////////////////
                    //}

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////

                    if (session != IntPtr.Zero)
                    {
                        UnsafeNativeMethods.sqlite3session_delete(session);
                        session = IntPtr.Zero;
                    }
                }
            }
            finally
            {
                //
                // NOTE: Everything should be fully disposed at this point.
                //
                disposed = true;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Destructor
        ~SQLiteSession()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    public sealed class SQLiteChangeSet : ISQLiteChangeSet, IDisposable
    {
        #region Private Data
        private SQLiteChangeSetIterator iterator;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        internal SQLiteChangeSet(
            SQLiteChangeSetIterator iterator
            )
        {
            this.iterator = iterator;
        }

        ///////////////////////////////////////////////////////////////////////






        ///////////////////////////////////////////////////////////////////////




        ///////////////////////////////////////////////////////////////////////

        #region ISQLiteChangeSet Members
        public bool? IsPatchSet
        {
            get { throw new NotImplementedException(); }
        }

        ///////////////////////////////////////////////////////////////////////

        public void Apply(
            SQLiteConnection connection,
            ConflictDelegate conflictCallback
            )
        {
            throw new NotImplementedException();
        }

        ///////////////////////////////////////////////////////////////////////

        public void Apply(
            SQLiteConnection connection,
            ConflictDelegate conflictCallback,
            TableFilterDelegate tableFilterCallback
            )
        {
            throw new NotImplementedException();
        }

        ///////////////////////////////////////////////////////////////////////

        public ISQLiteChangeSet Invert()
        {
            throw new NotImplementedException();
        }

        ///////////////////////////////////////////////////////////////////////

        public ISQLiteChangeSet CombineWith(
            ISQLiteChangeSet changeSet
            )
        {
            throw new NotImplementedException();
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable Members
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IDisposable "Pattern" Members
        private bool disposed;
        private void CheckDisposed() /* throw */
        {
#if THROW_ON_DISPOSED
            if (disposed)
            {
                throw new ObjectDisposedException(
                    typeof(SQLiteChangeSet).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        private /* protected virtual */ void Dispose(bool disposing)
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

                        if (iterator != null)
                        {
                            iterator.Dispose();
                            iterator = null;
                        }
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////
                }
            }
            finally
            {
                //
                // NOTE: Everything should be fully disposed at this point.
                //
                disposed = true;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Destructor
        ~SQLiteChangeSet()
        {
            Dispose(false);
        }
        #endregion
    }

    ///////////////////////////////////////////////////////////////////////////

    public sealed class SQLiteChangeSetEnumerator : IEnumerator<ISQLiteChangeSetMetadataItem>
    {
        public SQLiteChangeSetEnumerator(
            ISQLiteChangeSet changeSet
            )
        {

        }

        #region IEnumerator<ISQLiteChangeSetMetadataItem> Members

        public ISQLiteChangeSetMetadataItem Current
        {
            get { throw new NotImplementedException(); }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region IEnumerator Members

        object Collections.IEnumerator.Current
        {
            get { throw new NotImplementedException(); }
        }

        public bool MoveNext()
        {
            throw new NotImplementedException();
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    ///////////////////////////////////////////////////////////////////////////

    public sealed class SQLiteChangeSetMetadataItem : ISQLiteChangeSetMetadataItem
    {
        internal SQLiteChangeSetMetadataItem(IntPtr iterator)
        {

        }

        #region ISQLiteChangeSetMetadataItem Members

        public SQLiteAuthorizerActionCode OperationCode
        {
            get { throw new NotImplementedException(); }
        }

        public string TableName
        {
            get { throw new NotImplementedException(); }
        }

        public int NumberOfColumns
        {
            get { throw new NotImplementedException(); }
        }

        public bool Indirect
        {
            get { throw new NotImplementedException(); }
        }

        public bool[] PrimaryKeyColumns
        {
            get { throw new NotImplementedException(); }
        }

        public SQLiteValue GetOldValue(int columnIndex)
        {
            throw new NotImplementedException();
        }

        public SQLiteValue GetNewValue(int columnIndex)
        {
            throw new NotImplementedException();
        }

        public SQLiteValue GetConflictValue(int columnIndex)
        {
            throw new NotImplementedException();
        }

        public int NumberOfForeignKeyConflicts
        {
            get { throw new NotImplementedException(); }
        }

        #endregion
    }

}
