﻿// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using Microsoft.Practices.Unity.Utility;
using Unity;
using Unity.Builder;
using Unity.Policy;

namespace Microsoft.Practices.ObjectBuilder2
{
    /// <summary>
    /// Represents a strategy for mapping build keys in the build up operation.
    /// </summary>
    public class BuildKeyMappingStrategy : BuilderStrategy
    {
        /// <summary>
        /// Called during the chain of responsibility for a build operation.  Looks for the <see cref="IBuildKeyMappingPolicy"/>
        /// and if found maps the build key for the current operation.
        /// </summary>
        /// <param name="context">The context for the operation.</param>
        public override void PreBuildUp(IBuilderContext context)
        {
            Guard.ArgumentNotNull(context, "context");

            var policy = context.Policies.Get<IBuildKeyMappingPolicy>(context.BuildKey);

            if (policy != null)
            {
                context.BuildKey = policy.Map(context.BuildKey, context);
            }
        }
    }
}
