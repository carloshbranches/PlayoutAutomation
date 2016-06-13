﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using TAS.Common;
using TAS.Server.Database;
using TAS.Server.Interfaces;
using System.IO;
using Newtonsoft.Json;
using TAS.Server.Common;

namespace TAS.Server
{
    public class AnimationDirectory : MediaDirectory, IAnimationDirectory
    {
        public readonly CasparServer Server;
        public AnimationDirectory(CasparServer server, MediaManager manager) : base(manager)
        {
            Server = server;
        }

        public override void Initialize()
        {
            if (!_isInitialized)
            {
                DirectoryName = "Animacje";
                this.Load<AnimatedMedia>(Server.Id);
                base.Initialize();
                Debug.WriteLine(Server.AnimationFolder, "AnimationDirectory initialized");
            }
        }

        public override void Refresh()
        {

        }

        protected override bool AcceptFile(string fullPath)
        {
            return FileUtils.AnimationFileTypes.Contains(Path.GetExtension(fullPath).ToLowerInvariant());
        }

        protected override IMedia AddFile(string fullPath, DateTime created = default(DateTime), DateTime lastWriteTime = default(DateTime), Guid guid = default(Guid))
        {
            AnimatedMedia newMedia = _files.Values.FirstOrDefault(m => fullPath.Equals(m.FullPath)) as AnimatedMedia;
            if (newMedia == null && AcceptFile(fullPath))
            {
                newMedia = (AnimatedMedia)CreateMedia(fullPath, guid);
                newMedia.MediaName = Path.GetFileNameWithoutExtension(fullPath).ToUpper();
                newMedia.LastUpdated = lastWriteTime == default(DateTime) ? File.GetLastWriteTimeUtc(fullPath) : lastWriteTime;
                newMedia.MediaType = TMediaType.Animation;
                newMedia.MediaStatus = TMediaStatus.Available;
                newMedia.Save();
            }
            return newMedia;
        }

        protected override IMedia CreateMedia(string fullPath, Guid guid)
        {
            return new AnimatedMedia(this, guid, 0) { FullPath = fullPath, Verified = true };
        }

        public override void MediaRemove(IMedia media)
        {
            var tm = media as AnimatedMedia;
            if (tm != null)
            {
                tm.MediaStatus = TMediaStatus.Deleted;
                tm.Verified = false;
                tm.Save();
            }
            base.MediaRemove(media);
        }

        public override bool DeleteMedia(IMedia media)
        {
            if (base.DeleteMedia(media))
            {
                MediaRemove(media);
                return true;
            }
            return false;
        }

        public override void SweepStaleMedia() { }

    }
}
