﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
using Ark.Tools.FtpClient.Core;
using Ark.Tools.Http;

using Flurl.Http;
using Flurl.Http.Configuration;

using NLog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Ark.Tools.FtpClient.FtpProxy
{

    public sealed class FtpClientProxy : IFtpClientPool
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        private IFtpClientProxyConfig _config;
        private readonly TokenProvider _tokenProvider;
        private readonly ConnectionInfo _connectionInfo;

        private readonly IFlurlClient _client;

        [Obsolete("Use the constructor with URI", false)]
        public FtpClientProxy(IFtpClientProxyConfig config, IFlurlClientFactory client, string host, NetworkCredential credentials)
            : this(config,client, new TokenProvider(config), host,credentials)
        {
        }

        public FtpClientProxy(IFtpClientProxyConfig config, IFlurlClientFactory client, Uri uri, NetworkCredential credentials)
            : this(config, client, new TokenProvider(config), uri, credentials)
        {
        }

        [Obsolete("Use the constructor with URI", false)]
        internal FtpClientProxy(IFtpClientProxyConfig config, IFlurlClientFactory client, TokenProvider tokenProvider, string host, NetworkCredential credentials)
        {
            _init(config, host, null, credentials);

            _tokenProvider = tokenProvider;

            _client = _initClient(client);

            _connectionInfo = _initConnectionInfo();
        }

        internal FtpClientProxy(IFtpClientProxyConfig config, IFlurlClientFactory client, TokenProvider tokenProvider, Uri uri, NetworkCredential credentials)
        {
            _init(config, null, uri, credentials);

            _tokenProvider = tokenProvider;

            _client = _initClient(client);

            _connectionInfo = _initConnectionInfo();               
        }

        public string Host { get; private set; }
        public Uri Uri { get; private set; }

        public NetworkCredential Credentials { get; private set; }

        class DownloadFileResult
        {
            public byte[] Content { get; set; }
        }

        class ConnectionInfo
        {
            public string Host { get; set; }
            public Uri Uri { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        class ListingRequest
        {
            public ConnectionInfo Info { get; set; }
            public int? DegreeOfParallelism { get; set; }
            public bool? Recursive { get; set; }
            public string[] Paths { get; set; }
        }

        /// <summary>
        /// Download a file.
        /// </summary>
        /// <param name="path">The path to the file</param>
        /// <param name="ctk"></param>
        /// <returns>
        /// The byte[] of the contents of the file.
        /// </returns>
        public async Task<byte[]> DownloadFileAsync(string path, CancellationToken ctk = default(CancellationToken))
        {
            var tok = await _getAccessToken(ctk).ConfigureAwait(false);

            var res = await _client.Request("v2", "DownloadFile")
                .SetQueryParam("filePath", path)
                .WithOAuthBearerToken(tok)
                .PostJsonAsync(_connectionInfo, ctk)
                .ReceiveJson<DownloadFileResult>()
                ;

            return res.Content;
        }

        /// <summary>
        /// List all entries of a folder.
        /// </summary>
        /// <param name="path">The folder path to list</param>
        /// <param name="ctk"></param>
        /// <returns>
        /// All entries found (files, folders, symlinks)
        /// </returns>
        /// <exception cref="System.NotImplementedException"></exception>
        public async Task<IEnumerable<FtpEntry>> ListDirectoryAsync(string path = null, CancellationToken ctk = default(CancellationToken))
        {
            var tok = await _getAccessToken(ctk).ConfigureAwait(false);
            
            var res = await _client.Request("v2", "ListFolder")
                .WithOAuthBearerToken(tok)
                .PostJsonAsync(new ListingRequest
                {
                    Info = _connectionInfo,
                    Paths = path != null ? new[] { path } : null,
                    Recursive = false,
                }, ctk)
                .ReceiveJson<IEnumerable<FtpEntry>>()
                ;

            return res;
        }

        /// <summary>
        /// List a directory recursively and returns the files found.
        /// </summary>
        /// <param name="startPath">The directory to list recursively</param>
        /// <param name="skipFolder">Predicate returns true for folders that are to be skipped.</param>
        /// <param name="ctk"></param>
        /// <returns>
        /// The files found.
        /// </returns>
        /// <exception cref="System.NotImplementedException"></exception>
        public async Task<IEnumerable<FtpEntry>> ListFilesRecursiveAsync(string startPath = null, Predicate<FtpEntry> skipFolder = null, CancellationToken ctk = default(CancellationToken))
        {
            var tok = await _getAccessToken(ctk).ConfigureAwait(false);
            if (skipFolder == null) // no folders to skip, just recurse overall
            {
                var res = await _client.Request("v2", "ListFolder")
                    .WithOAuthBearerToken(tok)
                    .PostJsonAsync(new ListingRequest
                    {
                        Info = _connectionInfo,
                        Paths = startPath != null ? new[] { startPath } : null,
                        Recursive = true,
                        DegreeOfParallelism = _config.ListingDegreeOfParallelism
                    }, ctk)
                    .ReceiveJson<IEnumerable<FtpEntry>>()
                    ;
                
                return res.Where(e => !e.IsDirectory);
            }
            else
            {
                var entries = new List<IEnumerable<FtpEntry>>();

                var res = await _client.Request("v2", "ListFolder")
                    .WithOAuthBearerToken(tok)
                    .PostJsonAsync(new ListingRequest
                    {
                        Info = _connectionInfo,
                        Paths = startPath != null ? new[] { startPath } : null,
                        Recursive = false,
                    }, ctk)
                    .ReceiveJson<IEnumerable<FtpEntry>>()
                    ;

                entries.Add(res);

                var folders = res.Where(x => x.IsDirectory && !skipFolder(x)).ToArray();
                while (folders.Length > 0)
                {
                    var r = await _client.Request("v2", "ListFolder")
                        .WithOAuthBearerToken(tok)
                        .PostJsonAsync(new ListingRequest
                        {
                            Info = _connectionInfo,
                            Paths = folders.Select(f => f.FullPath).ToArray(),
                            Recursive = false,
                        }, ctk)
                        .ReceiveJson<IEnumerable<FtpEntry>>()
                        ;

                    entries.Add(r);
                    folders = r.Where(x => x.IsDirectory && !skipFolder(x)).ToArray();
                }

                return entries.SelectMany(x => x.Where(e => !e.IsDirectory));
            }            
        }

        private Task<string> _getAccessToken(CancellationToken ctk = default(CancellationToken))
        {
            return _tokenProvider.GetToken(ctk);
        }

        private void _init(IFtpClientProxyConfig config, string host, Uri uri, NetworkCredential credentials)
        {
            this._config = config;
            this.Host = host;
            this.Uri = uri;
            this.Credentials = credentials;
        }
        private IFlurlClient _initClient(IFlurlClientFactory client)
        {
            var flurlClient = client.Get(_config.FtpProxyWebInterfaceBaseUri)
                .Configure(c =>
                {
                    c.HttpClientFactory = new UntrustedCertClientFactory();
                    c.ConnectionLeaseTimeout = TimeSpan.FromMinutes(30);
                })
                .WithHeader("Accept", "application/json, text/json")
                .WithHeader("Accept-Encoding", "gzip, deflate")
                .WithTimeout(TimeSpan.FromMinutes(20))
                .AllowHttpStatus(HttpStatusCode.NotFound)
                ;

            flurlClient.BaseUrl = _config.FtpProxyWebInterfaceBaseUri.ToString();

            return flurlClient;
        }
        private ConnectionInfo _initConnectionInfo()
        {
            return new ConnectionInfo
            {
                Host = this.Host,
                Uri = this.Uri,
                Username = this.Credentials.UserName,
                Password = this.Credentials.Password,
            };
        }


        public Task UploadFileAsync(string path, byte[] content, CancellationToken ctk = default)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

    }
}
