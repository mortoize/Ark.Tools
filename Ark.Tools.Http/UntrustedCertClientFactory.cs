﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
using Flurl.Http.Configuration;
using System.Net.Http;

namespace Ark.Tools.Http
{
    public class UntrustedCertClientFactory : DefaultHttpClientFactory
    {
        public override HttpMessageHandler CreateMessageHandler()
        {
            var res = base.CreateMessageHandler();
            if (res is HttpClientHandler httpHandler)
                httpHandler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
            return res;
        }
    }
}
