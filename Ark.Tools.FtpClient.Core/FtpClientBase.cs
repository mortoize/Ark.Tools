﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
using EnsureThat;
using NLog;
using Polly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Ark.Tools.FtpClient.Core
{
    public abstract class FtpClientBase : IFtpClient
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        public Uri Uri { get; }
        public NetworkCredential Credentials { get; }
        public int MaxListingRecursiveParallelism { get; }

        public FtpConfig FtpConfig { get; }

        protected FtpClientBase(FtpConfig ftpConfig)
            : this(ftpConfig, 3)
        {
        }

        protected FtpClientBase(FtpConfig ftpConfig, int maxListingRecursiveParallelism)
        {
            EnsureArg.IsNotNull(ftpConfig);
            EnsureArg.IsNotNull(ftpConfig.Uri);
            EnsureArg.IsNotNull(ftpConfig.Credentials);

            Uri = ftpConfig.Uri;
            Credentials = ftpConfig.Credentials;
            MaxListingRecursiveParallelism = maxListingRecursiveParallelism;

            FtpConfig = ftpConfig;
        }

        public abstract Task<byte[]> DownloadFileAsync(string path, CancellationToken ctk = default);
        public abstract Task<IEnumerable<FtpEntry>> ListDirectoryAsync(string path = "./", CancellationToken ctk = default);
        public abstract Task UploadFileAsync(string path, byte[] content, CancellationToken ctk = default);
        
        public virtual async Task<IEnumerable<FtpEntry>> ListFilesRecursiveAsync(string startPath = "./", Predicate<FtpEntry>? skipFolder = null, CancellationToken ctk = default)
        {
            _logger.Trace("List files starting from path: {Path}", startPath);
            startPath ??= "./";

            if (skipFolder == null)
                skipFolder = x => false;

            Stack<FtpEntry> pendingFolders = new Stack<FtpEntry>();
            IEnumerable<FtpEntry> files = new List<FtpEntry>();
            List<Task<IEnumerable<FtpEntry>>> running = new List<Task<IEnumerable<FtpEntry>>>();

            async Task<IEnumerable<FtpEntry>> listFolderAsync(string path, CancellationToken ct)
            {
                var retrier = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(new[]
                    {
                        TimeSpan.FromSeconds(1),
                    }, (ex, ts) =>
                    {
                        _logger.Warn(ex, "Failed to list folder {Path}. Try again in {Sleep} ...", path, ts);
                    });

                return await retrier.ExecuteAsync(async ct1 =>
                {
                    return await this.ListDirectoryAsync(path, ct1);
                }, ct);

                
            }

            void startListing(string path)
            {
                running.Add(Task.Run(() => listFolderAsync(path, ctk), ctk));
            }

            startListing(startPath);

            try
            {
                while (running.Count > 0)
                {
                    var t = await Task.WhenAny(running);

                    var list = await t;
                    running.Remove(t); // remove only if successful

                    foreach (var d in list.Where(x => x.IsDirectory && !x.Name.Equals(".") && !x.Name.Equals("..")))
                    {
                        if (skipFolder.Invoke(d))
                            _logger.Info("Skipping folder: {Path}", d.FullPath);
                        else
                            pendingFolders.Push(d);
                    }

                    files = files.Concat(list.Where(x => !x.IsDirectory).ToList());

                    while (pendingFolders.Count > 0 && running.Count < this.MaxListingRecursiveParallelism)
                        startListing(pendingFolders.Pop().FullPath);                
                }
            }
            catch
            {
                await Task.WhenAll(running); // this still contains the failed one 
                throw;
            }


            return files;
        }
    }
}