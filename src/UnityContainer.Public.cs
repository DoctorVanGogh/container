﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Builder;
using Unity.Extension;
using Unity.Extensions;
using Unity.Lifetime;
using Unity.Registration;
using Unity.Resolution;
using Unity.Storage;
using Unity.Strategies;

namespace Unity
{
    public partial class UnityContainer
    {
        #region Constructors

        /// <summary>
        /// Create a default <see cref="UnityContainer"/>.
        /// </summary>
        public UnityContainer()
        {
            // Root of the hierarchy
            _root = this;

            // Lifetime Container
            LifetimeContainer = new LifetimeContainer(this);

            // Registry
            InitializeRootRegistry();

            /////////////////////////////////////////////////////////////

            // Context
            _context = new ContainerContext(this);

            // Build Strategies
            _strategies = new StagedStrategyChain<BuilderStrategy, UnityBuildStage>
            {
                {new LifetimeStrategy(), UnityBuildStage.Lifetime},             // Lifetime
                {new BuildKeyMappingStrategy(), UnityBuildStage.TypeMapping},   // Mapping
                {new BuildPlanStrategy(), UnityBuildStage.Creation}             // Build
            };

            // Update on change
            _strategies.Invalidated += (s, e) => _strategiesChain = _strategies.ToArray(); 
            _strategiesChain = _strategies.ToArray();


            // Default Policies and Strategies
            SetDefaultPolicies(this);
        }

        #endregion


        #region Registrations

        /// <inheritdoc />
        public bool IsRegistered(Type type, string name)
        {
            int hashCode = NamedType.GetHashCode(type, name);

            // Iterate through hierarchy and check if exists
            for (var container = this; null != container; container = container._parent)
            {
                // Skip to parent if no registry
                if (null == container._metadata)
                    continue;

                // Look for exact match
                if (container._registry.Contains(hashCode, type))
                    return true;
            }

            return false;
        }

        /// <inheritdoc />
        public IEnumerable<IContainerRegistration> Registrations
        {
            get
            {
                var set = new QuickSet<Type>();

                // IUnityContainer
                yield return new ContainerRegistrationStruct
                {
                    RegisteredType = typeof(IUnityContainer),
                    MappedToType = typeof(UnityContainer),
                    LifetimeManager = _containerManager
                };
                set.Add(_root._registry.Entries[1].HashCode, typeof(IUnityContainer));

                // IUnityContainerAsync
                yield return new ContainerRegistrationStruct
                {
                    RegisteredType = typeof(IUnityContainerAsync),
                    MappedToType = typeof(UnityContainer),
                    LifetimeManager = _containerManager
                };
                set.Add(_root._registry.Entries[2].HashCode, typeof(IUnityContainerAsync));

                Type type;
                // Scan containers for explicit registrations
                for (var container = this; null != container; container = container._parent)
                {
                    // Skip to parent if no registrations
                    if (null == container._metadata) continue;

                    // Hold on to registries
                    var registry = container._registry;

                    for (var i = 0; i < registry.Count; i++)
                    {
                        if (!(registry.Entries[i].Value is ExplicitRegistration registration)) continue;

                        type = registry.Entries[i].Key.Type;
                        if (set.Add(registry.Entries[i].HashCode, type))
                        {
                            yield return new ContainerRegistrationStruct
                            {
                                RegisteredType  = type,
                                Name            = registry.Entries[i].Key.Name,
                                LifetimeManager = registration.LifetimeManager,
                                MappedToType    = registration.Type,
                            };
                        }
                    }
                }
            }
        }

        #endregion


        #region Extension Management

        /// <summary>
        /// Add an extension to the container.
        /// </summary>
        /// <param name="extension"><see cref="UnityContainerExtension"/> to add.</param>
        /// <returns>The <see cref="IUnityContainer"/> object that this method was called on (this in C#, Me in Visual Basic).</returns>
        public IUnityContainer AddExtension(IUnityContainerExtensionConfigurator extension)
        {
            lock (LifetimeContainer)
            {
                if (null == _extensions)
                    _extensions = new List<IUnityContainerExtensionConfigurator>();

                _extensions.Add(extension ?? throw new ArgumentNullException(nameof(extension)));
            }
            (extension as UnityContainerExtension)?.InitializeExtension(_context);

            return this;
        }

        /// <summary>
        /// Resolve access to a configuration interface exposed by an extension.
        /// </summary>
        /// <remarks>Extensions can expose configuration interfaces as well as adding
        /// strategies and policies to the container. This method walks the list of
        /// added extensions and returns the first one that implements the requested type.
        /// </remarks>
        /// <param name="configurationInterface"><see cref="Type"/> of configuration interface required.</param>
        /// <returns>The requested extension's configuration interface, or null if not found.</returns>
        public object Configure(Type configurationInterface)
        {
#if NETSTANDARD1_0 || NETCOREAPP1_0
            return _extensions?.FirstOrDefault(ex => configurationInterface.GetTypeInfo()
                                                                           .IsAssignableFrom(ex.GetType()
                                                                           .GetTypeInfo()));
#else
            return _extensions?.FirstOrDefault(ex => configurationInterface.IsAssignableFrom(ex.GetType()));
#endif
        }

        #endregion


        #region IDisposable Implementation

        /// <summary>
        /// Dispose this container instance.
        /// </summary>
        /// <remarks>
        /// Disposing the container also disposes any child containers,
        /// and disposes any instances whose lifetimes are managed
        /// by the container.
        /// </remarks>
        public void Dispose()
        {
            List<Exception> exceptions = null;

            try
            {
                _parent?.LifetimeContainer.Remove(this);
                LifetimeContainer.Dispose();
            }
            catch (Exception exception)
            {
                exceptions = new List<Exception> { exception };
            }

            if (null != _extensions)
            {
                foreach (IDisposable disposable in _extensions.OfType<IDisposable>()
                                                              .ToList())
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception e)
                    {
                        if (null == exceptions) exceptions = new List<Exception>();
                        exceptions.Add(e);
                    }
                }

                _extensions = null;
            }

            if (null != exceptions && exceptions.Count == 1)
            {
                throw exceptions[0];
            }
            else if (null != exceptions && exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }
        }

        #endregion
    }
}
