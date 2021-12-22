﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
using Ark.Tools.FtpClient.Core;
using FluentFTP;
using NLog;
using Sunlighter.AsyncQueueLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ark.Tools.FtpClient.FluentFtp
{
    public class FluentFtpClientConnection : FtpClientConnectionBase
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly FluentFTP.IFtpClient _client;

        public FluentFtpClientConnection(string host, NetworkCredential credential) 
            : base(host, credential)
        {
            _client = _getClientFromHost();
        }

        public FluentFtpClientConnection(Uri uri, NetworkCredential credential)
            : base(uri, credential)
        {
            _client = _getClientFromUri();
        }

        public override async ValueTask ConnectAsync(CancellationToken ctk)
        {
            if (_client.IsConnected)
                return;

            await _client.ConnectAsync(ctk);
        }

        public override async ValueTask DisconnectAsync(CancellationToken ctk = default)
        {
            if (!_client.IsConnected)
                return;

            await _client.DisconnectAsync(ctk);
        }

        public override async Task<byte[]> DownloadFileAsync(string path, CancellationToken ctk = default)
        {
            var res = await _client.DownloadAsync(path);
            return res;
        }

        public override ValueTask<bool> IsConnectedAsync(CancellationToken ctk = default)
        {
            return new ValueTask<bool>(_client.IsConnected);
        }

        public override async Task<IEnumerable<FtpEntry>> ListDirectoryAsync(string path = null, CancellationToken ctk = default)
        {
            var lst = await _client.GetListingAsync(path, FtpListOption.Auto);
            var res = lst.Select(x => new FtpEntry()
            {
                FullPath = x.FullName,
                IsDirectory = x.Type == FtpFileSystemObjectType.Directory,
                Modified = x.Modified,
                Name = x.Name,
                Size = x.Size
            }).ToList();

            return res;
        }

        public override async Task UploadFileAsync(string path, byte[] content, CancellationToken ctk = default)
        {
            await _client.UploadAsync(content, path, token:ctk);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _client?.Dispose();
        }

        private FluentFTP.IFtpClient _getClientFromHost()
        {
            var client = new FluentFTP.FtpClient(Host)
            {
                Credentials = Credentials,
                SocketKeepAlive = true,
                //SocketPollInterval = 1000,
                //ConnectTimeout = 5000,
                //DataConnectionConnectTimeout = 5000,
            };

            return client;
        }

        private FluentFTP.IFtpClient _getClientFromUri()
        {
            var client = new FluentFTP.FtpClient(Uri)
            {
                Credentials = Credentials,
                SocketKeepAlive = true,
                //SocketPollInterval = 1000,
                //ConnectTimeout = 5000,
                //DataConnectionConnectTimeout = 5000,
            };

            return client;
        }
    }
}
