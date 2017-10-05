/********************************************************
 * ADO.NET 2.0 Data Provider for SQLite Version 3.X
 * Written by Joe Mistachkin (joe@mistachkin.com)
 *
 * Released to the public domain, use at your own risk!
 ********************************************************/

using System.Collections.Generic;

namespace System.Data.SQLite
{
    public enum SQLiteChangeSetConflictType
    {
        Data = 1,
        NotFound = 2,
        Conflict = 3,
        Constraint = 4,
        ForeignKey = 5
    }

    public enum SQLiteChangeSetConflictResult
    {
        Omit = 0,
        Replace = 1,
        Abort = 2
    }

    public delegate void TableFilterDelegate(
        object context,
        string tableName
    );

    public delegate SQLiteChangeSetConflictResult ConflictDelegate(
        object context,
        SQLiteChangeSetConflictType type,
        ISQLiteChangeSetMetadataItem item
    );

    public interface ISQLiteChangeSet
    {
        bool IsPatchSet { get; }

        void Apply(SQLiteConnection connection, ConflictDelegate conflictCallback);
        void Apply(SQLiteConnection connection, ConflictDelegate conflictCallback, TableFilterDelegate tableCallback);

        ISQLiteChangeSet Invert();

        ISQLiteChangeSet CombineWith(ISQLiteChangeSet changeSet);
    }

    public interface ISQLiteChangeGroup
    {
        void Add(ISQLiteChangeSet changeSet);
        ISQLiteChangeSet CreateChangeSet();
    }

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
        void SetTableFilter(TableFilterDelegate tableCallback, object context);

        ISQLiteChangeSet CreateChangeSet();
        ISQLiteChangeSet CreatePatchSet();

        void LoadDifferencesFromTable(string fromDatabaseName, string tableName);
    }
}
