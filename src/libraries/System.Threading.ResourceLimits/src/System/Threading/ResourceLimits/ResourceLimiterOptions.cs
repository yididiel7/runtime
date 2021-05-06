// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading.ResourceLimits
{
    public abstract class ResourceLimiterOptions
    {
        public long ResourceLimit { get; set; }
        public ResourceDepletedMode DepletedMode { get; set; }
    }
}
