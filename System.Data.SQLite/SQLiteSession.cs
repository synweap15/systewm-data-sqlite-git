/********************************************************
 * ADO.NET 2.0 Data Provider for SQLite Version 3.X
 * Written by Joe Mistachkin (joe@mistachkin.com)
 *
 * Released to the public domain, use at your own risk!
 ********************************************************/

using System.Collections;
using System.Collections.Generic;

#if DEBUG
using System.Diagnostics;
#endif

using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;

namespace System.Data.SQLite
{
    #region Session Extension Enumerations
    /// <summary>
    /// This enumerated type represents a type of conflict seen when apply
    /// changes from a change set or patch set.
    /// </summary>
    public enum SQLiteChangeSetConflictType
    {
        /// <summary>
        /// This value is seen when processing a DELETE or UPDATE change if a
        /// row with the required PRIMARY KEY fields is present in the
        /// database, but one or more other (non primary-key) fields modified
        /// by the update do not contain the expected "before" values.
        /// </summary>
        Data = 1,

        /// <summary>
        /// This value is seen when processing a DELETE or UPDATE change if a
        /// row with the required PRIMARY KEY fields is not present in the
        /// database.  There is no conflicting row in this case.
        ///
        /// The results of invoking the
        /// <see cref="ISQLiteChangeSetMetadataItem.GetConflictValue" />
        /// method are undefined.
        /// </summary>
        NotFound = 2,

        /// <summary>
        /// This value is seen when processing an INSERT change if the
        /// operation would result in duplicate primary key values.
        /// The conflicting row in this case is the database row with the
        /// matching primary key.
        /// </summary>
        Conflict = 3,

        /// <summary>
        /// If a non-foreign key constraint violation occurs while applying a
        /// change (i.e. a UNIQUE, CHECK or NOT NULL constraint), the conflict
        /// callback will see this value.
        ///
        /// There is no conflicting row in this case. The results of invoking
        /// the <see cref="ISQLiteChangeSetMetadataItem.GetConflictValue" />
        /// method are undefined.
        /// </summary>
        Constraint = 4,

        /// <summary>
        /// If foreign key handling is enabled, and applying a changes leaves
        /// the database in a state containing foreign key violations, this
        /// value will be seen exactly once before the changes are committed.
        /// If the conflict handler
        /// <see cref="SQLiteChangeSetConflictResult.Omit" />, the changes,
        /// including those that caused the foreign key constraint violation,
        /// are committed. Or, if it returns
        /// <see cref="SQLiteChangeSetConflictResult.Abort" />, the changes are
        /// rolled back.
        ///
        /// No current or conflicting row information is provided. The only
        /// method it is possible to call on the supplied
        /// <see cref="ISQLiteChangeSetMetadataItem" /> object is
        /// <see cref="ISQLiteChangeSetMetadataItem.NumberOfForeignKeyConflicts" />.
        /// </summary>
        ForeignKey = 5
    }

    ///////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This enumerated type represents the result of a user-defined conflict
    /// resolution callback.
    /// </summary>
    public enum SQLiteChangeSetConflictResult
    {
        /// <summary>
        /// If a conflict callback returns this value no special action is
        /// taken. The change that caused the conflict is not applied. The
        /// application of changes continues with the next change.
        /// </summary>
        Omit = 0,

        /// <summary>
        /// This value may only be returned from a conflict callback if the
        /// type of conflict was <see cref="SQLiteChangeSetConflictType.Data" />
        /// or <see cref="SQLiteChangeSetConflictType.Conflict" />. If this is
        /// not the case, any changes applied so far are rolled back and the
        /// call to
        /// <see cref="ISQLiteChangeSet.Apply(SessionConflictCallback,SessionTableFilterCallback,object)" />
        /// will raise a <see cref="SQLiteException" /> with an error code of
        /// <see cref="SQLiteErrorCode.Misuse" />.
        ///
        /// If this value is returned for a
        /// <see cref="SQLiteChangeSetConflictType.Data" /> conflict, then the
        /// conflicting row is either updated or deleted, depending on the type
        /// of change.
        ///
        /// If this value is returned for a
        /// <see cref="SQLiteChangeSetConflictType.Conflict" /> conflict, then
        /// the conflicting row is removed from the database and a second
        /// attempt to apply the change is made. If this second attempt fails,
        /// the original row is restored to the database before continuing.
        /// </summary>
        Replace = 1,

        /// <summary>
        /// If this value is returned, any changes applied so far are rolled
        /// back and the call to
        /// <see cref="ISQLiteChangeSet.Apply(SessionConflictCallback,SessionTableFilterCallback,object)" />
        /// will raise a <see cref="SQLiteException" /> with an error code of
        /// <see cref="SQLiteErrorCode.Abort" />.
        /// </summary>
        Abort = 2
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region Session Extension Delegates
    /// <summary>
    /// This callback is invoked when a determination must be made about
    /// whether changes to a specific table should be tracked -OR- applied.
    /// It will not be called for tables that are already attached to a
    /// <see cref="ISQLiteSession" />.
    /// </summary>
    /// <param name="clientData">
    /// The optional application-defined context data that was originally
    /// passed to the <see cref="ISQLiteSession.SetTableFilter" /> or
    /// <see cref="ISQLiteChangeSet.Apply(SessionConflictCallback,SessionTableFilterCallback,object)" />
    /// methods.  This value may be null.
    /// </param>
    /// <param name="name">
    /// The name of the table.
    /// </param>
    /// <returns>
    /// Non-zero if changes to the table should be considered; otherwise,
    /// zero.  Throwing an exception from this callback will result in
    /// undefined behavior.
    /// </returns>
    public delegate bool SessionTableFilterCallback(
        object clientData,
        string name
    );

    ///////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This callback is invoked when there is a conflict while apply changes
    /// to a database.
    /// </summary>
    /// <param name="clientData">
    /// The optional application-defined context data that was originally
    /// passed to the
    /// <see cref="ISQLiteChangeSet.Apply(SessionConflictCallback,SessionTableFilterCallback,object)" />
    /// method.  This value may be null.
    /// </param>
    /// <param name="type">
    /// The type of this conflict.
    /// </param>
    /// <param name="item">
    /// The <see cref="ISQLiteChangeSetMetadataItem" /> object associated with
    /// this conflict.  This value may not be null; however, only properties
    /// that are applicable to the conflict type will be available.  Further
    /// information on this is available within the descriptions of the
    /// available <see cref="SQLiteChangeSetConflictType" /> values.
    /// </param>
    /// <returns>
    /// A <see cref="SQLiteChangeSetConflictResult" /> value that indicates the
    /// action to be taken in order to resolve the conflict.  Throwing an
    /// exception from this callback will result in undefined behavior.
    /// </returns>
    public delegate SQLiteChangeSetConflictResult SessionConflictCallback(
        object clientData,
        SQLiteChangeSetConflictType type,
        ISQLiteChangeSetMetadataItem item
    );
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ISQLiteChangeSet Interface
    public interface ISQLiteChangeSet :
        IEnumerable<ISQLiteChangeSetMetadataItem>, IDisposable
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
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ISQLiteChangeGroup Interface
    public interface ISQLiteChangeGroup : IDisposable
    {
        void AddChangeSet(byte[] rawData);
        void AddChangeSet(Stream stream);

        void CreateChangeSet(ref byte[] rawData);
        void CreateChangeSet(Stream stream);
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ISQLiteChangeSetMetadataItem Interface
    public interface ISQLiteChangeSetMetadataItem : IDisposable
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
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ISQLiteSession Interface
    public interface ISQLiteSession : IDisposable
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

    #region SQLiteSessionHelpers Class
    internal static class SQLiteSessionHelpers
    {
        #region Public Methods
        public static void CheckRawData(
            byte[] rawData
            )
        {
            if (rawData == null)
                throw new ArgumentNullException("rawData");

            if (rawData.Length == 0)
            {
                throw new ArgumentException(
                    "empty change set data", "rawData");
            }
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteConnectionLock Class
    internal abstract class SQLiteConnectionLock : IDisposable
    {
        #region Private Constants
        private const string LockNopSql = "SELECT 1;";

        ///////////////////////////////////////////////////////////////////////

        private const string StatementMessageFormat =
            "Connection lock object was {0} with statement {1}";
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Data
        private SQLiteConnectionHandle handle;
        private SQLiteConnectionFlags flags;

        ///////////////////////////////////////////////////////////////////////

        private IntPtr statement;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteConnectionLock(
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags
            )
        {
            this.handle = handle;
            this.flags = flags;

            Lock();
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Protected Methods
        protected SQLiteConnectionHandle GetHandle()
        {
            return handle;
        }

        ///////////////////////////////////////////////////////////////////////

        protected SQLiteConnectionFlags GetFlags()
        {
            return flags;
        }

        ///////////////////////////////////////////////////////////////////////

        protected IntPtr GetIntPtr()
        {
            if (handle == null)
            {
                throw new InvalidOperationException(
                    "Connection lock object has an invalid handle.");
            }

            IntPtr handlePtr = handle;

            if (handlePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Connection lock object has an invalid handle pointer.");
            }

            return handlePtr;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Methods
        public void Lock()
        {
            CheckDisposed();

            if (statement != IntPtr.Zero)
                return;

            IntPtr pSql = IntPtr.Zero;

            try
            {
                int nSql = 0;

                pSql = SQLiteString.Utf8IntPtrFromString(LockNopSql, ref nSql);

                IntPtr pRemain = IntPtr.Zero;
                int nRemain = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3_prepare_interop(
                    GetIntPtr(), pSql, nSql, ref statement, ref pRemain,
                    ref nRemain);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3_prepare_interop");
            }
            finally
            {
                if (pSql != IntPtr.Zero)
                {
                    SQLiteMemory.Free(pSql);
                    pSql = IntPtr.Zero;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////

        public void Unlock()
        {
            CheckDisposed();

            if (statement == IntPtr.Zero)
                return;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3_finalize_interop(
                statement);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3_finalize_interop");

            statement = IntPtr.Zero;
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
                    typeof(SQLiteConnectionLock).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        protected virtual void Dispose(bool disposing)
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

                    if (statement != IntPtr.Zero)
                    {
                        //
                        // NOTE: This should never happen.  This object was
                        //       disposed (or finalized) without the Unlock
                        //       method being called first.
                        //
                        try
                        {
                            if (HelperMethods.LogPrepare(GetFlags()))
                            {
                                /* throw */
                                SQLiteLog.LogMessage(
                                    SQLiteErrorCode.Misuse,
                                    HelperMethods.StringFormat(
                                    CultureInfo.CurrentCulture,
                                    StatementMessageFormat, disposing ?
                                        "disposed" : "finalized",
                                    statement));
                            }
                        }
                        catch
                        {
                            // do nothing.
                        }

#if DEBUG
                        Debugger.Break();
#endif
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
        ~SQLiteConnectionLock()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeSetIterator Class
    internal class SQLiteChangeSetIterator : IDisposable
    {
        #region Private Data
        private IntPtr iterator;

        ///////////////////////////////////////////////////////////////////////

        private bool ownHandle;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Protected Constructors
        protected SQLiteChangeSetIterator(
            IntPtr iterator,
            bool ownHandle
            )
        {
            this.iterator = iterator;
            this.ownHandle = ownHandle;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        internal void CheckHandle()
        {
            if (iterator == IntPtr.Zero)
                throw new InvalidOperationException("iterator is not open");
        }

        ///////////////////////////////////////////////////////////////////////

        internal IntPtr GetIntPtr()
        {
            return iterator;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Methods
        public bool Next()
        {
            CheckDisposed();
            CheckHandle();

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_next(
                iterator);

            switch (rc)
            {
                case SQLiteErrorCode.Ok:
                    {
                        throw new SQLiteException(SQLiteErrorCode.Ok,
                            "sqlite3changeset_next: unexpected result Ok");
                    }
                case SQLiteErrorCode.Row:
                    {
                        return true;
                    }
                case SQLiteErrorCode.Done:
                    {
                        return false;
                    }
                default:
                    {
                        throw new SQLiteException(rc, "sqlite3changeset_next");
                    }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Static "Factory" Methods
        public static SQLiteChangeSetIterator Attach(
            IntPtr iterator
            )
        {
            return new SQLiteChangeSetIterator(iterator, false);
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

        protected virtual void Dispose(bool disposing)
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
                        if (ownHandle)
                        {
                            UnsafeNativeMethods.sqlite3changeset_finalize(
                                iterator);
                        }

                        iterator = IntPtr.Zero;
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

    #region SQLiteMemoryChangeSetIterator Class
    internal sealed class SQLiteMemoryChangeSetIterator :
        SQLiteChangeSetIterator
    {
        #region Private Data
        private IntPtr pData;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        private SQLiteMemoryChangeSetIterator(
            IntPtr pData,
            IntPtr iterator,
            bool ownHandle
            )
            : base(iterator, ownHandle)
        {
            this.pData = pData;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Static "Factory" Methods
        public static SQLiteMemoryChangeSetIterator Create(
            byte[] rawData
            )
        {
            SQLiteSessionHelpers.CheckRawData(rawData);

            SQLiteMemoryChangeSetIterator result = null;
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

                result = new SQLiteMemoryChangeSetIterator(
                    pData, iterator, true);
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

        #region IDisposable "Pattern" Members
        private bool disposed;
        private void CheckDisposed() /* throw */
        {
#if THROW_ON_DISPOSED
            if (disposed)
            {
                throw new ObjectDisposedException(
                    typeof(SQLiteMemoryChangeSetIterator).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        protected override void Dispose(bool disposing)
        {
            //
            // NOTE: Must dispose of the base class first (leaky abstraction)
            //       because it contains the iterator handle, which must be
            //       closed *prior* to freeing the underlying memory.
            //
            base.Dispose(disposing);

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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteStreamChangeSetIterator Class
    internal sealed class SQLiteStreamChangeSetIterator :
        SQLiteChangeSetIterator
    {
        #region Private Data
        private SQLiteStreamAdapter streamAdapter;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        private SQLiteStreamChangeSetIterator(
            SQLiteStreamAdapter streamAdapter,
            IntPtr iterator,
            bool ownHandle
            )
            : base(iterator, ownHandle)
        {
            this.streamAdapter = streamAdapter;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Static "Factory" Methods
        public static SQLiteStreamChangeSetIterator Create(
            Stream stream,
            SQLiteConnectionFlags flags
            )
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            SQLiteStreamAdapter streamAdapter = null;
            SQLiteStreamChangeSetIterator result = null;
            IntPtr iterator = IntPtr.Zero;

            try
            {
                streamAdapter = new SQLiteStreamAdapter(stream, flags);

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_start_strm(
                    ref iterator, streamAdapter.GetInputDelegate(), IntPtr.Zero);

                if (rc != SQLiteErrorCode.Ok)
                {
                    throw new SQLiteException(
                        rc, "sqlite3changeset_start_strm");
                }

                result = new SQLiteStreamChangeSetIterator(
                    streamAdapter, iterator, true);
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

                    if (streamAdapter != null)
                    {
                        streamAdapter.Dispose();
                        streamAdapter = null;
                    }
                }
            }

            return result;
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
                    typeof(SQLiteStreamChangeSetIterator).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        protected override void Dispose(bool disposing)
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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteStreamAdapter Class
    internal sealed class SQLiteStreamAdapter : IDisposable
    {
        #region Private Data
        private Stream stream; /* EXEMPT: NOT OWNED */
        private SQLiteConnectionFlags flags;

        ///////////////////////////////////////////////////////////////////////

        private UnsafeNativeMethods.xSessionInput xInput;
        private UnsafeNativeMethods.xSessionOutput xOutput;
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

        #region Private Methods
        private SQLiteConnectionFlags GetFlags()
        {
            return flags;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Methods
        public UnsafeNativeMethods.xSessionInput GetInputDelegate()
        {
            CheckDisposed();

            if (xInput == null)
                xInput = new UnsafeNativeMethods.xSessionInput(Input);

            return xInput;
        }

        ///////////////////////////////////////////////////////////////////////

        public UnsafeNativeMethods.xSessionOutput GetOutputDelegate()
        {
            CheckDisposed();

            if (xOutput == null)
                xOutput = new UnsafeNativeMethods.xSessionOutput(Output);

            return xOutput;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Native Callback Methods
        private SQLiteErrorCode Input(
            IntPtr context,
            IntPtr pData,
            ref int nData
            )
        {
            try
            {
                Stream localStream = stream;

                if (localStream == null)
                    return SQLiteErrorCode.Misuse;

                if (nData > 0)
                {
                    byte[] bytes = new byte[nData];
                    int nRead = localStream.Read(bytes, 0, nData);

                    if ((nRead > 0) && (pData != IntPtr.Zero))
                        Marshal.Copy(bytes, 0, pData, nRead);

                    nData = nRead;
                }

                return SQLiteErrorCode.Ok;
            }
            catch (Exception e)
            {
                try
                {
                    if (HelperMethods.LogCallbackExceptions(GetFlags()))
                    {
                        SQLiteLog.LogMessage(
                            SQLiteBase.COR_E_EXCEPTION,
                            HelperMethods.StringFormat(
                            CultureInfo.CurrentCulture,
                            UnsafeNativeMethods.ExceptionMessageFormat,
                            "xSessionInput", e)); /* throw */
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

        private SQLiteErrorCode Output(
            IntPtr context,
            IntPtr pData,
            int nData
            )
        {
            try
            {
                Stream localStream = stream;

                if (localStream == null)
                    return SQLiteErrorCode.Misuse;

                if (nData > 0)
                {
                    byte[] bytes = new byte[nData];

                    if (pData != IntPtr.Zero)
                        Marshal.Copy(pData, bytes, 0, nData);

                    localStream.Write(bytes, 0, nData);
                }

                localStream.Flush();

                return SQLiteErrorCode.Ok;
            }
            catch (Exception e)
            {
                try
                {
                    if (HelperMethods.LogCallbackExceptions(GetFlags()))
                    {
                        SQLiteLog.LogMessage(
                            SQLiteBase.COR_E_EXCEPTION,
                            HelperMethods.StringFormat(
                            CultureInfo.CurrentCulture,
                            UnsafeNativeMethods.ExceptionMessageFormat,
                            "xSessionOutput", e)); /* throw */
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

                        if (xInput != null)
                            xInput = null;

                        if (xOutput != null)
                            xOutput = null;

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

    #region SQLiteSessionStreamManager Class
    internal sealed class SQLiteSessionStreamManager : IDisposable
    {
        #region Private Data
        private Dictionary<Stream, SQLiteStreamAdapter> streamAdapters;
        private SQLiteConnectionFlags flags;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteSessionStreamManager(
            SQLiteConnectionFlags flags
            )
        {
            this.flags = flags;

            InitializeStreamAdapters();
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void InitializeStreamAdapters()
        {
            if (streamAdapters != null)
                return;

            streamAdapters = new Dictionary<Stream, SQLiteStreamAdapter>();
        }

        ///////////////////////////////////////////////////////////////////////

        private void DisposeStreamAdapters()
        {
            if (streamAdapters == null)
                return;

            foreach (KeyValuePair<Stream, SQLiteStreamAdapter> pair
                    in streamAdapters)
            {
                SQLiteStreamAdapter streamAdapter = pair.Value;

                if (streamAdapter == null)
                    continue;

                streamAdapter.Dispose();
            }

            streamAdapters.Clear();
            streamAdapters = null;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Methods
        public SQLiteStreamAdapter GetAdapter(
            Stream stream
            )
        {
            CheckDisposed();

            if (stream == null)
                return null;

            SQLiteStreamAdapter streamAdapter;

            if (streamAdapters.TryGetValue(stream, out streamAdapter))
                return streamAdapter;

            streamAdapter = new SQLiteStreamAdapter(stream, flags);
            streamAdapters.Add(stream, streamAdapter);

            return streamAdapter;
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
                    typeof(SQLiteSessionStreamManager).Name);
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

                        DisposeStreamAdapters();
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
        ~SQLiteSessionStreamManager()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeGroup Class
    internal sealed class SQLiteChangeGroup : ISQLiteChangeGroup
    {
        #region Private Data
        private SQLiteSessionStreamManager streamManager;
        private SQLiteConnectionFlags flags;

        ///////////////////////////////////////////////////////////////////////

        private IntPtr changeGroup;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteChangeGroup(
            SQLiteConnectionFlags flags
            )
        {
            this.flags = flags;

            InitializeHandle();
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void CheckHandle()
        {
            if (changeGroup == IntPtr.Zero)
                throw new InvalidOperationException("change group not open");
        }

        ///////////////////////////////////////////////////////////////////////

        private void InitializeHandle()
        {
            if (changeGroup != IntPtr.Zero)
                return;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changegroup_new(
                ref changeGroup);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changegroup_new");
        }

        ///////////////////////////////////////////////////////////////////////

        private void InitializeStreamManager()
        {
            if (streamManager != null)
                return;

            streamManager = new SQLiteSessionStreamManager(flags);
        }

        ///////////////////////////////////////////////////////////////////////

        private SQLiteStreamAdapter GetStreamAdapter(
            Stream stream
            )
        {
            InitializeStreamManager();

            return streamManager.GetAdapter(stream);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ISQLiteChangeGroup Members
        public void AddChangeSet(
            byte[] rawData
            )
        {
            CheckDisposed();
            CheckHandle();

            SQLiteSessionHelpers.CheckRawData(rawData);

            IntPtr pData = IntPtr.Zero;

            try
            {
                int nData = 0;

                pData = SQLiteBytes.ToIntPtr(rawData, ref nData);

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changegroup_add(
                    changeGroup, nData, pData);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changegroup_add");
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

        public void AddChangeSet(
            Stream stream
            )
        {
            CheckDisposed();
            CheckHandle();

            if (stream == null)
                throw new ArgumentNullException("stream");

            SQLiteStreamAdapter streamAdapter = GetStreamAdapter(stream);

            if (streamAdapter == null)
            {
                throw new SQLiteException(
                    "could not get or create adapter for input stream");
            }

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changegroup_add_strm(
                changeGroup, streamAdapter.GetInputDelegate(), IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changegroup_add_strm");
        }

        ///////////////////////////////////////////////////////////////////////

        public void CreateChangeSet(
            ref byte[] rawData
            )
        {
            CheckDisposed();
            CheckHandle();

            SQLiteSessionHelpers.CheckRawData(rawData);

            IntPtr pData = IntPtr.Zero;

            try
            {
                int nData = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changegroup_output(
                    changeGroup, ref nData, ref pData);

                if (rc != SQLiteErrorCode.Ok)
                    throw new SQLiteException(rc, "sqlite3changegroup_output");

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

            if (stream == null)
                throw new ArgumentNullException("stream");

            SQLiteStreamAdapter streamAdapter = GetStreamAdapter(stream);

            if (streamAdapter == null)
            {
                throw new SQLiteException(
                    "could not get or create adapter for output stream");
            }

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changegroup_output_strm(
                changeGroup, streamAdapter.GetOutputDelegate(), IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changegroup_output_strm");
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
                    typeof(SQLiteChangeGroup).Name);
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

                        if (streamManager != null)
                        {
                            streamManager.Dispose();
                            streamManager = null;
                        }
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////

                    if (changeGroup != IntPtr.Zero)
                    {
                        UnsafeNativeMethods.sqlite3changegroup_delete(
                            changeGroup);

                        changeGroup = IntPtr.Zero;
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
        ~SQLiteChangeGroup()
        {
            Dispose(false);
        }
        #endregion
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteSession Class
    internal sealed class SQLiteSession : SQLiteConnectionLock, ISQLiteSession
    {
        #region Private Data
        private SQLiteSessionStreamManager streamManager;
        private string databaseName;

        ///////////////////////////////////////////////////////////////////////

        private IntPtr session;

        ///////////////////////////////////////////////////////////////////////

        private UnsafeNativeMethods.xSessionFilter xFilter;
        private SessionTableFilterCallback tableFilterCallback;
        private object tableFilterClientData;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteSession(
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags,
            string databaseName
            )
            : base(handle, flags)
        {
            this.databaseName = databaseName;

            InitializeHandle();
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

        private void InitializeHandle()
        {
            if (session != IntPtr.Zero)
                return;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_create(
                GetIntPtr(), SQLiteString.GetUtf8BytesFromString(databaseName),
                ref session);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3session_create");
        }

        ///////////////////////////////////////////////////////////////////////

        private UnsafeNativeMethods.xSessionFilter ApplyTableFilter(
            SessionTableFilterCallback callback, /* in: NULL OK */
            object clientData                    /* in: NULL OK */
            )
        {
            tableFilterCallback = callback;
            tableFilterClientData = clientData;

            if (callback == null)
            {
                if (xFilter != null)
                    xFilter = null;

                return null;
            }

            if (xFilter == null)
                xFilter = new UnsafeNativeMethods.xSessionFilter(Filter);

            return xFilter;
        }

        ///////////////////////////////////////////////////////////////////////

        private void InitializeStreamManager()
        {
            if (streamManager != null)
                return;

            streamManager = new SQLiteSessionStreamManager(GetFlags());
        }

        ///////////////////////////////////////////////////////////////////////

        private SQLiteStreamAdapter GetStreamAdapter(
            Stream stream
            )
        {
            InitializeStreamManager();

            return streamManager.GetAdapter(stream);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Native Callback Methods
        private int Filter(
            IntPtr context, /* NOT USED */
            IntPtr pTblName
            )
        {
            try
            {
                return tableFilterCallback(tableFilterClientData,
                    SQLiteString.StringFromUtf8IntPtr(pTblName)) ? 1 : 0;
            }
            catch (Exception e)
            {
                try
                {
                    if (HelperMethods.LogCallbackExceptions(GetFlags()))
                    {
                        SQLiteLog.LogMessage( /* throw */
                            SQLiteBase.COR_E_EXCEPTION,
                            HelperMethods.StringFormat(
                            CultureInfo.CurrentCulture,
                            UnsafeNativeMethods.ExceptionMessageFormat,
                            "xSessionFilter", e));
                    }
                }
                catch
                {
                    // do nothing.
                }
            }

            return 0;
        }
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

        public void AttachTable(
            string name /* in: NULL OK */
            )
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
            SessionTableFilterCallback callback, /* in: NULL OK */
            object clientData                    /* in: NULL OK */
            )
        {
            CheckDisposed();
            CheckHandle();

            UnsafeNativeMethods.sqlite3session_table_filter(
                session, ApplyTableFilter(callback, clientData), IntPtr.Zero);
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

            if (stream == null)
                throw new ArgumentNullException("stream");

            SQLiteStreamAdapter streamAdapter = GetStreamAdapter(stream);

            if (streamAdapter == null)
            {
                throw new SQLiteException(
                    "could not get or create adapter for output stream");
            }

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_changeset_strm(
                session, streamAdapter.GetOutputDelegate(), IntPtr.Zero);

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

            if (stream == null)
                throw new ArgumentNullException("stream");

            SQLiteStreamAdapter streamAdapter = GetStreamAdapter(stream);

            if (streamAdapter == null)
            {
                throw new SQLiteException(
                    "could not get or create adapter for output stream");
            }

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3session_patchset_strm(
                session, streamAdapter.GetOutputDelegate(), IntPtr.Zero);

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

            if (fromDatabaseName == null)
                throw new ArgumentNullException("fromDatabaseName");

            if (tableName == null)
                throw new ArgumentNullException("tableName");

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

                        if (xFilter != null)
                            xFilter = null;

                        if (streamManager != null)
                        {
                            streamManager.Dispose();
                            streamManager = null;
                        }
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////

                    if (session != IntPtr.Zero)
                    {
                        UnsafeNativeMethods.sqlite3session_delete(session);
                        session = IntPtr.Zero;
                    }

                    Unlock();
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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeSetBase Class
    internal class SQLiteChangeSetBase : SQLiteConnectionLock
    {
        #region Private Constructors
        internal SQLiteChangeSetBase(
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags
            )
            : base(handle, flags)
        {
            // do nothing.
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private ISQLiteChangeSetMetadataItem CreateMetadataItem(
            IntPtr iterator
            )
        {
            return new SQLiteChangeSetMetadataItem(
                SQLiteChangeSetIterator.Attach(iterator));
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Protected Methods
        protected UnsafeNativeMethods.xSessionFilter GetDelegate(
            SessionTableFilterCallback tableFilterCallback,
            object clientData
            )
        {
            if (tableFilterCallback == null)
                return null;

            UnsafeNativeMethods.xSessionFilter xFilter;

            xFilter = new UnsafeNativeMethods.xSessionFilter(
                delegate(IntPtr context, IntPtr pTblName)
            {
                try
                {
                    string name = SQLiteString.StringFromUtf8IntPtr(
                        pTblName);

                    return tableFilterCallback(clientData, name) ? 1 : 0;
                }
                catch (Exception e)
                {
                    try
                    {
                        if (HelperMethods.LogCallbackExceptions(GetFlags()))
                        {
                            SQLiteLog.LogMessage( /* throw */
                                SQLiteBase.COR_E_EXCEPTION,
                                HelperMethods.StringFormat(
                                CultureInfo.CurrentCulture,
                                UnsafeNativeMethods.ExceptionMessageFormat,
                                "xSessionFilter", e));
                        }
                    }
                    catch
                    {
                        // do nothing.
                    }
                }

                return 0;
            });

            return xFilter;
        }

        ///////////////////////////////////////////////////////////////////////

        protected UnsafeNativeMethods.xSessionConflict GetDelegate(
            SessionConflictCallback conflictCallback,
            object clientData
            )
        {
            if (conflictCallback == null)
                return null;

            UnsafeNativeMethods.xSessionConflict xConflict;

            xConflict = new UnsafeNativeMethods.xSessionConflict(
                delegate(IntPtr context,
                         SQLiteChangeSetConflictType type,
                         IntPtr iterator)
            {
                try
                {
                    ISQLiteChangeSetMetadataItem item = CreateMetadataItem(
                        iterator);

                    if (item == null)
                    {
                        throw new SQLiteException(
                            "could not create metadata item");
                    }

                    return conflictCallback(clientData, type, item);
                }
                catch (Exception e)
                {
                    try
                    {
                        if (HelperMethods.LogCallbackExceptions(GetFlags()))
                        {
                            SQLiteLog.LogMessage( /* throw */
                                SQLiteBase.COR_E_EXCEPTION,
                                HelperMethods.StringFormat(
                                CultureInfo.CurrentCulture,
                                UnsafeNativeMethods.ExceptionMessageFormat,
                                "xSessionConflict", e));
                        }
                    }
                    catch
                    {
                        // do nothing.
                    }
                }

                return SQLiteChangeSetConflictResult.Abort;
            });

            return xConflict;
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
                    typeof(SQLiteChangeSetBase).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

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
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////

                    Unlock();
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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteMemoryChangeSet Class
    internal sealed class SQLiteMemoryChangeSet :
        SQLiteChangeSetBase, ISQLiteChangeSet
    {
        #region Private Data
        private byte[] rawData;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        internal SQLiteMemoryChangeSet(
            byte[] rawData,
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags
            )
            : base(handle, flags)
        {
            this.rawData = rawData;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ISQLiteChangeSet Members
        public ISQLiteChangeSet Invert()
        {
            CheckDisposed();

            SQLiteSessionHelpers.CheckRawData(rawData);

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

                return new SQLiteMemoryChangeSet(
                    newData, GetHandle(), GetFlags());
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

            SQLiteSessionHelpers.CheckRawData(rawData);

            SQLiteMemoryChangeSet memoryChangeSet =
                changeSet as SQLiteMemoryChangeSet;

            if (memoryChangeSet == null)
            {
                throw new ArgumentException(
                    "not a memory based change set", "changeSet");
            }

            SQLiteSessionHelpers.CheckRawData(memoryChangeSet.rawData);

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

                return new SQLiteMemoryChangeSet(
                    newData, GetHandle(), GetFlags());
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

            SQLiteSessionHelpers.CheckRawData(rawData);

            if (conflictCallback == null)
                throw new ArgumentNullException("conflictCallback");

            UnsafeNativeMethods.xSessionFilter xFilter = GetDelegate(
                tableFilterCallback, clientData);

            UnsafeNativeMethods.xSessionConflict xConflict = GetDelegate(
                conflictCallback, clientData);

            IntPtr pData = IntPtr.Zero;

            try
            {
                int nData = 0;

                pData = SQLiteBytes.ToIntPtr(rawData, ref nData);

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_apply(
                    GetIntPtr(), nData, pData, xFilter, xConflict, IntPtr.Zero);

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
            return new SQLiteMemoryChangeSetEnumerator(rawData);
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

                        if (rawData != null)
                            rawData = null;
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////

                    Unlock();
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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteStreamChangeSet Class
    internal sealed class SQLiteStreamChangeSet :
        SQLiteChangeSetBase, ISQLiteChangeSet
    {
        #region Private Data
        private SQLiteStreamAdapter inputStreamAdapter;
        private SQLiteStreamAdapter outputStreamAdapter;
        private Stream inputStream;
        private Stream outputStream;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Constructors
        internal SQLiteStreamChangeSet(
            Stream inputStream,
            Stream outputStream,
            SQLiteConnectionHandle handle,
            SQLiteConnectionFlags flags
            )
            : base(handle, flags)
        {
            this.inputStream = inputStream;
            this.outputStream = outputStream;
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

            if (inputStreamAdapter == null)
            {
                inputStreamAdapter = new SQLiteStreamAdapter(
                    inputStream, GetFlags());
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

            if (outputStreamAdapter == null)
            {
                outputStreamAdapter = new SQLiteStreamAdapter(
                    outputStream, GetFlags());
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
                inputStreamAdapter.GetInputDelegate(), IntPtr.Zero,
                outputStreamAdapter.GetOutputDelegate(), IntPtr.Zero);

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
                inputStreamAdapter.GetInputDelegate(), IntPtr.Zero,
                streamChangeSet.inputStreamAdapter.GetInputDelegate(),
                IntPtr.Zero, outputStreamAdapter.GetOutputDelegate(),
                IntPtr.Zero);

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

            if (conflictCallback == null)
                throw new ArgumentNullException("conflictCallback");

            UnsafeNativeMethods.xSessionFilter xFilter = GetDelegate(
                tableFilterCallback, clientData);

            UnsafeNativeMethods.xSessionConflict xConflict = GetDelegate(
                conflictCallback, clientData);

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_apply_strm(
                GetIntPtr(), inputStreamAdapter.GetInputDelegate(), IntPtr.Zero,
                xFilter, xConflict, IntPtr.Zero);

            if (rc != SQLiteErrorCode.Ok)
                throw new SQLiteException(rc, "sqlite3changeset_apply_strm");
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerable<ISQLiteChangeSetMetadataItem> Members
        public IEnumerator<ISQLiteChangeSetMetadataItem> GetEnumerator()
        {
            return new SQLiteStreamChangeSetEnumerator(
                inputStream, GetFlags());
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

                        if (outputStream != null)
                            outputStream = null; /* NOT OWNED */

                        if (inputStream != null)
                            inputStream = null; /* NOT OWNED */
                    }

                    //////////////////////////////////////
                    // release unmanaged resources here...
                    //////////////////////////////////////

                    Unlock();
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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeSetEnumerator Class
    internal abstract class SQLiteChangeSetEnumerator :
        IEnumerator<ISQLiteChangeSetMetadataItem>
    {
        #region Private Data
        private SQLiteChangeSetIterator iterator;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteChangeSetEnumerator(
            SQLiteChangeSetIterator iterator
            )
        {
            SetIterator(iterator);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void CheckIterator()
        {
            if (iterator == null)
                throw new InvalidOperationException("iterator unavailable");

            iterator.CheckHandle();
        }

        ///////////////////////////////////////////////////////////////////////

        private void SetIterator(
            SQLiteChangeSetIterator iterator
            )
        {
            this.iterator = iterator;
        }

        ///////////////////////////////////////////////////////////////////////

        private void CloseIterator()
        {
            if (iterator != null)
            {
                iterator.Dispose();
                iterator = null;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Protected Methods
        protected void ResetIterator(
            SQLiteChangeSetIterator iterator
            )
        {
            CloseIterator();
            SetIterator(iterator);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerator<ISQLiteChangeSetMetadataItem> Members
        public ISQLiteChangeSetMetadataItem Current
        {
            get
            {
                CheckDisposed();

                return new SQLiteChangeSetMetadataItem(iterator);
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerator Members
        object Collections.IEnumerator.Current
        {
            get
            {
                CheckDisposed();

                return Current;
            }
        }

        ///////////////////////////////////////////////////////////////////////

        public bool MoveNext()
        {
            CheckDisposed();
            CheckIterator();

            return iterator.Next();
        }

        ///////////////////////////////////////////////////////////////////////

        public virtual void Reset()
        {
            CheckDisposed();

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
                    typeof(SQLiteChangeSetEnumerator).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        protected virtual void Dispose(bool disposing)
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

                        CloseIterator();
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

    #region SQLiteMemoryChangeSetEnumerator Class
    internal sealed class SQLiteMemoryChangeSetEnumerator :
        SQLiteChangeSetEnumerator
    {
        #region Private Data
        private byte[] rawData;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteMemoryChangeSetEnumerator(
            byte[] rawData
            )
            : base(SQLiteMemoryChangeSetIterator.Create(rawData))
        {
            this.rawData = rawData;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IEnumerator Overrides
        public override void Reset()
        {
            CheckDisposed();

            ResetIterator(SQLiteMemoryChangeSetIterator.Create(rawData));
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
                    typeof(SQLiteMemoryChangeSetEnumerator).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteStreamChangeSetEnumerator Class
    internal sealed class SQLiteStreamChangeSetEnumerator :
        SQLiteChangeSetEnumerator
    {
        #region Public Constructors
        public SQLiteStreamChangeSetEnumerator(
            Stream stream,
            SQLiteConnectionFlags flags
            )
            : base(SQLiteStreamChangeSetIterator.Create(stream, flags))
        {
            // do nothing.
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
                    typeof(SQLiteStreamChangeSetEnumerator).Name);
            }
#endif
        }

        ///////////////////////////////////////////////////////////////////////

        protected override void Dispose(bool disposing)
        {
            try
            {
                //if (!disposed)
                //{
                //    if (disposing)
                //    {
                //        ////////////////////////////////////
                //        // dispose managed resources here...
                //        ////////////////////////////////////
                //    }

                //    //////////////////////////////////////
                //    // release unmanaged resources here...
                //    //////////////////////////////////////
                //}
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
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region SQLiteChangeSetMetadataItem Class
    internal sealed class SQLiteChangeSetMetadataItem :
        ISQLiteChangeSetMetadataItem
    {
        #region Private Data
        private SQLiteChangeSetIterator iterator;
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        public SQLiteChangeSetMetadataItem(
            SQLiteChangeSetIterator iterator
            )
        {
            this.iterator = iterator;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Private Methods
        private void CheckIterator()
        {
            if (iterator == null)
                throw new InvalidOperationException("iterator unavailable");

            iterator.CheckHandle();
        }

        ///////////////////////////////////////////////////////////////////////

        private void PopulateOperationMetadata()
        {
            if ((tableName == null) || (numberOfColumns == null) ||
                (operationCode == null) || (indirect == null))
            {
                CheckIterator();

                IntPtr pTblName = IntPtr.Zero;
                SQLiteAuthorizerActionCode op = SQLiteAuthorizerActionCode.None;
                int bIndirect = 0;
                int nColumns = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_op(
                    iterator.GetIntPtr(), ref pTblName, ref nColumns, ref op,
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
                CheckIterator();

                IntPtr pPrimaryKeys = IntPtr.Zero;
                int nColumns = 0;

                SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_pk(
                    iterator.GetIntPtr(), ref pPrimaryKeys, ref nColumns);

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
                CheckIterator();

                int conflicts = 0;

                SQLiteErrorCode rc =
                    UnsafeNativeMethods.sqlite3changeset_fk_conflicts(
                        iterator.GetIntPtr(), ref conflicts);

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
            CheckIterator();

            IntPtr pValue = IntPtr.Zero;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_old(
                iterator.GetIntPtr(), columnIndex, ref pValue);

            return SQLiteValue.FromIntPtr(pValue);
        }

        ///////////////////////////////////////////////////////////////////////

        public SQLiteValue GetNewValue(
            int columnIndex
            )
        {
            CheckDisposed();
            CheckIterator();

            IntPtr pValue = IntPtr.Zero;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_new(
                iterator.GetIntPtr(), columnIndex, ref pValue);

            return SQLiteValue.FromIntPtr(pValue);
        }

        ///////////////////////////////////////////////////////////////////////

        public SQLiteValue GetConflictValue(
            int columnIndex
            )
        {
            CheckDisposed();
            CheckIterator();

            IntPtr pValue = IntPtr.Zero;

            SQLiteErrorCode rc = UnsafeNativeMethods.sqlite3changeset_conflict(
                iterator.GetIntPtr(), columnIndex, ref pValue);

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

                        if (iterator != null)
                            iterator = null; /* NOT OWNED */
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
