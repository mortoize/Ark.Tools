﻿using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Threading.Tasks;

namespace Ark.AspNetCore.CommaSeparatedParameters
{
    public class SeparatedPathValueProviderFactory : IValueProviderFactory
    {
        private readonly char _separator;
        private readonly string _key;

        public SeparatedPathValueProviderFactory(char separator) : this(null, separator)
        {
        }

        public SeparatedPathValueProviderFactory(string key, char separator)
        {
            _separator = separator;
            _key = key;
        }


        public Task CreateValueProviderAsync(ValueProviderFactoryContext context)
        {
            context.ValueProviders.Insert(0, new SeparatedPathValueProvider(_key, _separator, context.ActionContext.RouteData.Values));
            return Task.CompletedTask;
        }
    }
}
