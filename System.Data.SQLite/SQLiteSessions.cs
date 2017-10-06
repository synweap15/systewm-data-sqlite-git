/********************************************************
 * ADO.NET 2.0 Data Provider for SQLite Version 3.X
 * Written by Joe Mistachkin (joe@mistachkin.com)
 *
 * Released to the public domain, use at your own risk!
 ********************************************************/

using System.Collections;
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
    public delegate bool SessionTableFilterCallback(
        object clientData,
        string name
    );

    ///////////////////////////////////////////////////////////////////////////

    public delegate SQLiteChangeSetConflictResult SessionConflictCallback(
        object clientData,
        SQLiteChangeSetConflictType type,
        ISQLiteChangeSetMetadataItem item
    );
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region Session Extension Interfaces
    public interface ISQLiteChangeSet
    {
        ISQLiteChangeSet Invert();
        ISQLiteChangeSet CombineWith(ISQLiteChangeSet changeSet);

        void Apply(
            SessionConflictCallback conflictCallback,
            object clientData
        );

        void Apply(
            SessionConflictCallback conflictCallback,
            SessionTableFilterCallback tableFilterCallback,
            object clientData
        );
    }

    ///////////////////////////////////////////////////////////////////////////

    public interface ISQLiteChangeGroup
    {
        void AddChangeSet(byte[] rawData);
        void AddChangeSet(Stream stream);

        void CreateChangeSet(ref byte[] rawData);
        void CreateChangeSet(Stream stream);
    }

    ///////////////////////////////////////////////////////////////////////////

    public interface ISQLiteChangeSetMetadataItem
    {
        string TableName { get; }
        int NumberOfColumns { get; }
        SQLiteAuthorizerActionCode OperationCode { get; }
        bool Indirect { get; }

        bool[] PrimaryKeyColumns { get; }

        int NumberOfForeignKeyConflicts { get; }

        SQLiteValue GetOldValue(int columnIndex);
        SQLiteValue GetNewValue(int columnIndex);
        SQLiteValue GetConflictValue(int columnIndex);
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

        void SetTableFilter(
            SessionTableFilterCallback callback,
            object clientData
        );

        void CreateChangeSet(ref byte[] rawData);
        void CreateChangeSet(Stream stream);

        void CreatePatchSet(ref byte[] rawData);
        void CreatePatchSet(Stream stream);

        void LoadDifferencesFromTable(
            string fromDatabaseName,
            string tableName
        );
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

        #region Private Methods
        private void CheckHandle()
        {
            if (iterator == IntPtr.Zero)
                throw new InvalidOperationException("iterator is not open");
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

        ///////////////////////////////////////////////////////////////////////

        public static SQLiteChangeSetIterator Create(
            Stream stream,
            SQLiteConnectionFlags flags
            )
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            SQLiteChangeSetIterator result = null;
            IntPtr iterator = IntPtr.Zero;

            try
            {
                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_start_strm(
                    ref iterator, new SQLiteStreamAdapter(stream, flags).xInput,
                    IntPtr.Zero);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changeset_start_strm");

                result = new SQLiteChangeSetIterator(IntPtr.Zero, iterator);
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
                }
            }

            return result;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Methods
        public void Next()
        {
            CheckDisposed();
            CheckHandle();

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_next(
                iterator);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changeset_next");
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
                            stream = null; /* NOT OWNED */
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
        private SQLiteConnectionHandle handle;
        private SQLiteConnectionFlags flags;
        private string databaseName;
        private IntPtr session;

        ///////////////////////////////////////////////////////////////////////

        private SessionTableFilterCallback tableFilterCallback;
        private object tableFilterClientData;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        internal SQLiteSession(
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags,
            string databaseName
            )
        {
            this.handle = handle;
            this.flags = flags;
            this.databaseName = databaseName;

            UnsafeNativeMethods.sqlite3session_create(
                handle, SQLiteString.GetUtf8BytesFromString(databaseName),
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
            return tableFilterCallback(tableFilterClientData,
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
            SessionTableFilterCallback callback,
            object clientData
            )
        {
            CheckDisposed();
            CheckHandle();

            this.tableFilterCallback = callback;
            this.tableFilterClientData = clientData;

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
            Stream stream
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
            Stream stream
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

    #region SQLiteMemoryChangeSet Class
    public sealed class SQLiteMemoryChangeSet :
        ISQLiteChangeSet, IEnumerable<ISQLiteChangeSetMetadataItem>,
        IDisposable
    {
        #region Private Data
        private byte[] rawData;
        private SQLiteConnectionHandle handle;
        private SQLiteConnectionFlags flags;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        internal SQLiteMemoryChangeSet(
            byte[] rawData,
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags
            )
        {
            this.rawData = rawData;
            this.handle= handle;
            this.flags = flags;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void CheckRawData()
        {
            if (rawData == null)
                throw new InvalidOperationException("no change set data");
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ISQLiteChangeSet Members
        public ISQLiteChangeSet Invert()
        {
            CheckDisposed();
            CheckRawData();

            IntPtr pInData = IntPtr.Zero;
            IntPtr pOutData = IntPtr.Zero;

            try
            {
                int nInData = 0;

                pInData = SQLiteBytes.ToIntPtr(rawData, ref nInData);

                int nOutData = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_invert(
                    nInData, pInData, ref nOutData, ref pOutData);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changeset_invert");

                byte[] newData = SQLiteBytes.FromIntPtr(pOutData, nOutData);

                return new SQLiteMemoryChangeSet(newData, handle, flags);
            }
            finally
            {
                if (pOutData != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pOutData);
                    pOutData = IntPtr.Zero;
                }

                if (pInData != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pInData);
                    pInData = IntPtr.Zero;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////

        public ISQLiteChangeSet CombineWith(
            ISQLiteChangeSet changeSet
            )
        {
            CheckDisposed();
            CheckRawData();

            SQLiteMemoryChangeSet memoryChangeSet =
                changeSet as SQLiteMemoryChangeSet;

            if (memoryChangeSet == null)
            {
                throw new ArgumentException(
                    "not a memory based change set", "changeSet");
            }

            memoryChangeSet.CheckRawData();

            IntPtr pInData1 = IntPtr.Zero;
            IntPtr pInData2 = IntPtr.Zero;
            IntPtr pOutData = IntPtr.Zero;

            try
            {
                int nInData1 = 0;

                pInData1 = SQLiteBytes.ToIntPtr(rawData, ref nInData1);

                int nInData2 = 0;

                pInData2 = SQLiteBytes.ToIntPtr(
                    memoryChangeSet.rawData, ref nInData2);

                int nOutData = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_concat(
                    nInData1, pInData1, nInData2, pInData2, ref nOutData,
                    ref pOutData);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changeset_concat");

                byte[] newData = SQLiteBytes.FromIntPtr(pOutData, nOutData);

                return new SQLiteMemoryChangeSet(newData, handle, flags);
            }
            finally
            {
                if (pOutData != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pOutData);
                    pOutData = IntPtr.Zero;
                }

                if (pInData2 != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pInData2);
                    pInData2 = IntPtr.Zero;
                }

                if (pInData1 != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pInData1);
                    pInData1 = IntPtr.Zero;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////

        public void Apply(
            SessionConflictCallback conflictCallback,
            object clientData
            )
        {
            CheckDisposed();

            Apply(conflictCallback, null, clientData);
        }

        ///////////////////////////////////////////////////////////////////////

        public void Apply(
            SessionConflictCallback conflictCallback,
            SessionTableFilterCallback tableFilterCallback,
            object clientData
            )
        {
            CheckDisposed();
            CheckRawData();

            if (conflictCallback == null)
                throw new ArgumentNullException("conflictCallback");

            UnsafeNativeMethods.xSessionFilter xFilter = null;

            if (tableFilterCallback != null)
            {
                xFilter = new UnsafeNativeMethods.xSessionFilter(
                    delegate(IntPtr context, byte[] tblName)
                {
                    try
                    {
                        string name = SQLiteString.GetStringFromUtf8Bytes(
                            tblName);

                        return tableFilterCallback(clientData, name) ? 1 : 0;
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
                                    "Caught exception in \"xFilter\" method: {0}",
                                    e)); /* throw */
                            }
                        }
                        catch
                        {
                            // do nothing.
                        }
                    }

                    return 0;
                });
            }

            UnsafeNativeMethods.xSessionConflict xConflict;

            xConflict = new UnsafeNativeMethods.xSessionConflict(
                delegate(IntPtr context,
                         SQLiteChangeSetConflictType type,
                         IntPtr iterator)
            {
                ISQLiteChangeSetMetadataItem item = null;

                try
                {
                    return conflictCallback(clientData, type, item);
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
                                "Caught exception in \"xConflict\" method: {0}",
                                e)); /* throw */
                        }
                    }
                    catch
                    {
                        // do nothing.
                    }
                }

                return SQLiteChangeSetConflictResult.Abort;
            });

            IntPtr pData = IntPtr.Zero;

            try
            {
                int nData = 0;

                pData = SQLiteBytes.ToIntPtr(rawData, ref nData);

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_apply(
                    handle, nData, pData, xFilter, xConflict, IntPtr.Zero);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changeset_apply");
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
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerable<ISQLiteChangeSetMetadataItem> Members
        public IEnumerator<ISQLiteChangeSetMetadataItem> GetEnumerator()
        {
            return new SQLiteChangeSetEnumerator(rawData);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerable Members
        IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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
                    typeof(SQLiteMemoryChangeSet).Name);
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

                        if (rawData != null)
                            rawData = null;
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
        ~SQLiteMemoryChangeSet()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteStreamChangeSet Class
    public sealed class SQLiteStreamChangeSet :
        ISQLiteChangeSet, IEnumerable<ISQLiteChangeSetMetadataItem>,
        IDisposable
    {
        #region Private Data
        private Stream inputStream;
        private Stream outputStream;
        private SQLiteConnectionHandle handle;
        private SQLiteConnectionFlags flags;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        internal SQLiteStreamChangeSet(
            Stream inputStream,
            Stream outputStream,
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags
            )
        {
            this.inputStream = inputStream;
            this.outputStream = outputStream;
            this.handle = handle;
            this.flags = flags;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void CheckInputStream()
        {
            if (inputStream == null)
            {
                throw new InvalidOperationException(
                    "input stream unavailable");
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private void CheckOutputStream()
        {
            if (outputStream == null)
            {
                throw new InvalidOperationException(
                    "output stream unavailable");
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ISQLiteChangeSet Members
        public ISQLiteChangeSet Invert()
        {
            CheckDisposed();
            CheckInputStream();
            CheckOutputStream();

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_invert_strm(
                new SQLiteStreamAdapter(inputStream, flags).xInput, IntPtr.Zero,
                new SQLiteStreamAdapter(outputStream, flags).xOutput, IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changeset_invert_strm");

            return null;
        }

        ///////////////////////////////////////////////////////////////////////

        public ISQLiteChangeSet CombineWith(
            ISQLiteChangeSet changeSet
            )
        {
            CheckDisposed();
            CheckInputStream();
            CheckOutputStream();

            SQLiteStreamChangeSet streamChangeSet =
                changeSet as SQLiteStreamChangeSet;

            if (streamChangeSet == null)
            {
                throw new ArgumentException(
                    "not a stream based change set", "changeSet");
            }

            streamChangeSet.CheckInputStream();

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_concat_strm(
                new SQLiteStreamAdapter(inputStream, flags).xInput, IntPtr.Zero,
                new SQLiteStreamAdapter(streamChangeSet.inputStream,
                streamChangeSet.flags).xInput, IntPtr.Zero,
                new SQLiteStreamAdapter(outputStream, flags).xOutput, IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changeset_concat_strm");

            return null;
        }

        ///////////////////////////////////////////////////////////////////////

        public void Apply(
            SessionConflictCallback conflictCallback,
            object clientData
            )
        {
            CheckDisposed();

            Apply(conflictCallback, null, clientData);
        }

        ///////////////////////////////////////////////////////////////////////

        public void Apply(
            SessionConflictCallback conflictCallback,
            SessionTableFilterCallback tableFilterCallback,
            object clientData
            )
        {
            CheckDisposed();
            CheckInputStream();

            UnsafeNativeMethods.xSessionFilter xFilter;

            xFilter = new UnsafeNativeMethods.xSessionFilter(
                delegate(IntPtr context, byte[] tblName)
            {
                try
                {
                    string name = SQLiteString.GetStringFromUtf8Bytes(
                        tblName);

                    return tableFilterCallback(clientData, name) ? 1 : 0;
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
                                "Caught exception in \"xFilter\" method: {0}",
                                e)); /* throw */
                        }
                    }
                    catch
                    {
                        // do nothing.
                    }
                }

                return 0;
            });

            UnsafeNativeMethods.xSessionConflict xConflict;

            xConflict = new UnsafeNativeMethods.xSessionConflict(
                delegate(IntPtr context,
                         SQLiteChangeSetConflictType type,
                         IntPtr iterator)
            {
                ISQLiteChangeSetMetadataItem item = null;

                try
                {
                    return conflictCallback(clientData, type, item);
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
                                "Caught exception in \"xConflict\" method: {0}",
                                e)); /* throw */
                        }
                    }
                    catch
                    {
                        // do nothing.
                    }
                }

                return SQLiteChangeSetConflictResult.Abort;
            });

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_apply_strm(
                handle, new SQLiteStreamAdapter(inputStream, flags).xInput,
                IntPtr.Zero, xFilter, xConflict, IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changeset_apply_strm");
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerable<ISQLiteChangeSetMetadataItem> Members
        public IEnumerator<ISQLiteChangeSetMetadataItem> GetEnumerator()
        {
            return new SQLiteChangeSetEnumerator(inputStream, flags);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerable Members
        IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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
                    typeof(SQLiteStreamChangeSet).Name);
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

                        if (outputStream != null)
                            outputStream = null; /* NOT OWNED */

                        if (inputStream != null)
                            inputStream = null; /* NOT OWNED */
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
        ~SQLiteStreamChangeSet()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeSetEnumerator Class
    internal sealed class SQLiteChangeSetEnumerator :
        IEnumerator<ISQLiteChangeSetMetadataItem>
    {
        #region Private Data
        private SQLiteChangeSetIterator iterator;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteChangeSetEnumerator(
            byte[] rawData
            )
        {
            iterator = SQLiteChangeSetIterator.Create(rawData);
        }

        ///////////////////////////////////////////////////////////////////////

        public SQLiteChangeSetEnumerator(
            Stream stream,
            SQLiteConnectionFlags flags
            )
        {
            iterator = SQLiteChangeSetIterator.Create(stream, flags);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerator<ISQLiteChangeSetMetadataItem> Members
        public ISQLiteChangeSetMetadataItem Current
        {
            get { CheckDisposed(); throw new NotImplementedException(); }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerator Members
        object Collections.IEnumerator.Current
        {
            get { CheckDisposed(); throw new NotImplementedException(); }
        }

        ///////////////////////////////////////////////////////////////////////

        public bool MoveNext()
        {
            CheckDisposed(); throw new NotImplementedException();
        }

        ///////////////////////////////////////////////////////////////////////

        public void Reset()
        {
            CheckDisposed(); throw new NotImplementedException();
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
                    typeof(SQLiteChangeSetEnumerator).Name);
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
        ~SQLiteChangeSetEnumerator()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeSetMetadataItem Class
    public sealed class SQLiteChangeSetMetadataItem :
        ISQLiteChangeSetMetadataItem, IDisposable
    {
        #region Private Data
        private IntPtr iterator;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        internal SQLiteChangeSetMetadataItem(
            IntPtr iterator
            )
        {
            this.iterator = iterator;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void CheckHandle()
        {
            if (iterator == IntPtr.Zero)
                throw new InvalidOperationException("iterator is not open");
        }

        ///////////////////////////////////////////////////////////////////////

        private void PopulateOperationMetadata()
        {
            if ((tableName == null) || (numberOfColumns == null) ||
                (operationCode == null) || (indirect == null))
            {
                CheckHandle();

                IntPtr pTblName = IntPtr.Zero;
                SQLiteAuthorizerActionCode op = SQLiteAuthorizerActionCode.None;
                int bIndirect = 0;
                int nColumns = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_op(
                    iterator, ref pTblName, ref nColumns, ref op,
                    ref bIndirect);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changeset_op");

                tableName = SQLiteString.StringFromUtf8IntPtr(pTblName);
                numberOfColumns = nColumns;
                operationCode = op;
                indirect = (bIndirect != 0);
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private void PopulatePrimaryKeyColumns()
        {
            if (primaryKeyColumns == null)
            {
                CheckHandle();

                IntPtr pPrimaryKeys = IntPtr.Zero;
                int nColumns = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_pk(
                    iterator, ref pPrimaryKeys, ref nColumns);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changeset_pk");

                byte[] bytes = SQLiteBytes.FromIntPtr(pPrimaryKeys, nColumns);

                if (bytes != null)
                {
                    primaryKeyColumns = new bool[nColumns];

                    for (int index = 0; index < bytes.Length; index++)
                        primaryKeyColumns[index] = (bytes[index] != 0);
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private void PopulateNumberOfForeignKeyConflicts()
        {
            if (numberOfForeignKeyConflicts == null)
            {
                CheckHandle();

                int conflicts = 0;

                SQLiteErrorCode rc =
                    UnsafeNativeMethods.sqlite3changeset_fk_conflicts(
                        iterator, ref conflicts);

                if (rc != SQLiteErrorCode.Ok)
                {
                    throw new SQLiteException(rc,
                        "sqlite3changeset_fk_conflicts");
                }

                numberOfForeignKeyConflicts = conflicts;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ISQLiteChangeSetMetadataItem Members
        private string tableName;
        public string TableName
        {
            get
            {
                CheckDisposed();
                PopulateOperationMetadata();

                return tableName;
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private int? numberOfColumns;
        public int NumberOfColumns
        {
            get
            {
                CheckDisposed();
                PopulateOperationMetadata();

                return (int)numberOfColumns;
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private SQLiteAuthorizerActionCode? operationCode;
        public SQLiteAuthorizerActionCode OperationCode
        {
            get
            {
                CheckDisposed();
                PopulateOperationMetadata();

                return (SQLiteAuthorizerActionCode)operationCode;
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private bool? indirect;
        public bool Indirect
        {
            get
            {
                CheckDisposed();
                PopulateOperationMetadata();

                return (bool)indirect;
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private bool[] primaryKeyColumns;
        public bool[] PrimaryKeyColumns
        {
            get
            {
                CheckDisposed();
                PopulatePrimaryKeyColumns();

                return primaryKeyColumns;
            }
        }

        ///////////////////////////////////////////////////////////////////////

        private int? numberOfForeignKeyConflicts;
        public int NumberOfForeignKeyConflicts
        {
            get
            {
                CheckDisposed();
                PopulateNumberOfForeignKeyConflicts();

                return (int)numberOfForeignKeyConflicts;
            }
        }

        ///////////////////////////////////////////////////////////////////////

        public SQLiteValue GetOldValue(
            int columnIndex
            )
        {
            CheckDisposed();
            CheckHandle();

            IntPtr pValue = IntPtr.Zero;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_old(
                iterator, columnIndex, ref pValue);

            return SQLiteValue.FromIntPtr(pValue);
        }

        ///////////////////////////////////////////////////////////////////////

        public SQLiteValue GetNewValue(int columnIndex)
        {
            CheckDisposed();
            CheckHandle();

            IntPtr pValue = IntPtr.Zero;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_new(
                iterator, columnIndex, ref pValue);

            return SQLiteValue.FromIntPtr(pValue);
        }

        ///////////////////////////////////////////////////////////////////////

        public SQLiteValue GetConflictValue(int columnIndex)
        {
            CheckDisposed();
            CheckHandle();

            IntPtr pValue = IntPtr.Zero;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_conflict(
                iterator, columnIndex, ref pValue);

            return SQLiteValue.FromIntPtr(pValue);
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
                    typeof(SQLiteChangeSetMetadataItem).Name);
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

                        if (iterator != IntPtr.Zero)
                            iterator = IntPtr.Zero; /* NOT OWNED */
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
        ~SQLiteChangeSetMetadataItem()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion
}
