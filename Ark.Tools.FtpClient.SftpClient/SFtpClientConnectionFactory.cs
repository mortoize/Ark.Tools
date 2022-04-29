﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
using Ark.Tools.FtpClient.Core;
using EnsureThat;
using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Ark.Tools.FtpClient.SftpClient
{
    public sealed class SFtpClientConnectionFactory : IFtpClientConnectionFactory
    {
        [Obsolete("Use the constructor with URI", false)]
        public IFtpClientConnection Create(string host, NetworkCredential credentials)
        {
            EnsureArg.IsNotEmpty(host);
            EnsureArg.IsNotNull(credentials);

            int port = 2222;
            string h = host;
            var r = Regex.Match(host, @":\d+$");
            if (r.Success)
            {
                h = host.Substring(0, r.Index);
                port = Convert.ToInt16(host.Substring(r.Index + 1));
            }

            return new SftpClientConnection(h, credentials, port);
        }

        [Obsolete("Use the constructor with FtpConfig", false)]
        public IFtpClientConnection Create(Uri uri, NetworkCredential credentials)
        {
            EnsureArg.IsNotNull(uri);
            EnsureArg.IsNotNull(credentials);

            return new SftpClientConnection(uri, credentials);
        }

        public IFtpClientConnection Create(FtpConfig ftpConfig)
        {
            EnsureArg.IsNotNull(ftpConfig);
            EnsureArg.IsNotNull(ftpConfig.Uri);
            EnsureArg.IsNotNull(ftpConfig.Credentials);

            return new SftpClientConnection(ftpConfig);
        }
    }

}
