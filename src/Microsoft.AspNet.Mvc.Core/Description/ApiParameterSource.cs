// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.Mvc.Description
{
    public class ApiParameterSource
    {
        public static readonly ApiParameterSource Body = new ApiParameterSource("Body")
        {
        };

        public static readonly ApiParameterSource Header = new ApiParameterSource("Header")
        {
        };

        public static readonly ApiParameterSource Hidden = new ApiParameterSource("Hidden")
        {
        };

        public static readonly ApiParameterSource ModelBinding = new ApiParameterSource("ModelBinding")
        {
        };

        public static readonly ApiParameterSource Path = new ApiParameterSource("Path")
        {
        };

        public static readonly ApiParameterSource Query = new ApiParameterSource("Query")
        {
        };

        public static readonly ApiParameterSource Unknown = new ApiParameterSource("Unknown")
        {
        };

        public ApiParameterSource(string id)
        {
            Id = id;
        }

        public string Id { get; set; }
    }
}