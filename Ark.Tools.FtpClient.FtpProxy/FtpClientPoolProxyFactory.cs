﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
using Ark.Tools.FtpClient.Core;
using Ark.Tools.Http;

using EnsureThat;
using System;
using System.Net;

namespace Ark.Tools.FtpClient.FtpProxy
{

    public class FtpClientPoolProxyFactory : IFtpClientPoolFactory
    {
        private readonly IFtpClientProxyConfig _config;
        private readonly TokenProvider _tokenProvider;

        public FtpClientPoolProxyFactory(IFtpClientProxyConfig config)
        {
            EnsureArg.IsNotNull(config);
            _config = config;
            _tokenProvider = new TokenProvider(config);
        }

        public IFtpClientPool Create(int maxPoolSize, FtpConfig ftpConfig)
        {
            return new FtpClientProxy(_config, ArkFlurlClientFactory.Instance, _tokenProvider, ftpConfig);
        }
    }
}
