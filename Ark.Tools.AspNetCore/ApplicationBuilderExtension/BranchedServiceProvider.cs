﻿using System;

namespace Ark.AspNetCore.ApplicationBuilderExtension
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

        public object GetService(Type serviceType)
        {
            return _service.GetService(serviceType) ?? _parentService.GetService(serviceType);
        }
    }
}
