﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
using System;

namespace Ark.Tools.AspNetCore.NestedStartup
{
    internal class BranchedServiceProvider : IServiceProvider
    {
        private IServiceProvider _parentService;
        private IServiceProvider _service;

        public BranchedServiceProvider(IServiceProvider parentService, IServiceProvider service)
        {
            _parentService = parentService;
            _service = service;
        }

        public object? GetService(Type serviceType)
        {
            return _service.GetService(serviceType) ?? _parentService.GetService(serviceType);
        }
    }
}
