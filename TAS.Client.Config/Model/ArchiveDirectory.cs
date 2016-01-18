﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TAS.Server.Interfaces;

namespace TAS.Client.Config.Model
{
    public class ArchiveDirectory: IArchiveDirectoryConfig
    {
        ulong _idArchive;
        public ulong idArchive { get { return _idArchive; }  set { SetField(ref _idArchive, value); } }
        string _folder;
        public string Folder { get { return _folder; } set { SetField(ref _folder, value); } }
        protected bool _modified;
        public bool Modified { get { return _modified; } internal set { _modified = value; } }
        private bool _deleted;

        public void Delete()
        {
            _deleted = true;
        }

        public bool IsDeleted { get { return _deleted; } }

        public bool IsNew { get { return _idArchive == 0; } }

        protected virtual bool SetField<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Modified = true;
            return true;
        }

        public override string ToString()
        {
            return Folder;
        }
    }
}
