﻿// Copyright (c) 2018 Ark S.r.l. All rights reserved.
// Licensed under the MIT License. See LICENSE file for license information. 
namespace Ark.Tools.Solid
{
    public interface IContextProvider<TItem>
    {
        TItem Current { get; }
    }
}
