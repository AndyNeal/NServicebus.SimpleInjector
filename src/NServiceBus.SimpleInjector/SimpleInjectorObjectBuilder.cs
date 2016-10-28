namespace NServiceBus.ObjectBuilder.SimpleInjector
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NServiceBus.SimpleInjector;
    using global::SimpleInjector;
    using global::SimpleInjector.Extensions.ExecutionContextScoping;
    using Janitor;
    using System.Threading;
    using System.Collections;

    public class SimpleInjectorObjectBuilder : Common.IContainer
    {
        [SkipWeaving]
        Container container;

        bool isChildContainer;
        bool isBuilt = false;

        public SimpleInjectorObjectBuilder(Container parentContainer)
        {
            container = parentContainer.Clone();

            isChildContainer = true;
        }

        public SimpleInjectorObjectBuilder()
        {
            container = new Container();
            container.AllowToResolveArraysAndLists();
            container.Options.AllowOverridingRegistrations = true;
            container.Options.DefaultScopedLifestyle = new ExecutionContextScopeLifestyle();
            container.Options.AutoWirePropertiesImplicitly();

            container.BeginExecutionContextScope();
        }

        public void Dispose()
        {
            //Injected at compile time
        }

        void DisposeManaged()
        {
            var scope = container.GetCurrentExecutionContextScope();

            var scopeTemp = Interlocked.Exchange(ref scope, null);
            if (scopeTemp != null)
            {
                scopeTemp.Dispose();
            }

            if (!isChildContainer)
            {
                var temp = Interlocked.Exchange(ref container, null);
                if (temp != null)
                {
                    temp.Dispose();
                }
            }
        }

        public Common.IContainer BuildChildContainer()
        {
            return new SimpleInjectorObjectBuilder(container);
        }

        public object Build(Type typeToBuild)
        {
            if (!HasComponent(typeToBuild))
            {
                throw new ActivationException("The requested type is not registered yet");
            }

            isBuilt = true;

            return container.GetInstance(typeToBuild);
        }

        public IEnumerable<object> BuildAll(Type typeToBuild)
        {
            if (HasComponent(typeToBuild))
            {
                isBuilt = true;

                try
                {
                    return container.GetAllInstances(typeToBuild);
                }
                catch (Exception)
                {
                    // Urgh!
                    return new[] { container.GetInstance(typeToBuild) };
                }
            }

            return new object[] { };
        }

        public void Configure(Type component, DependencyLifecycle dependencyLifecycle)
        {
            EnsureContainerIsConfigurable();

            var registration = GetRegistrationFromDependencyLifecycle(dependencyLifecycle, component);

            foreach (var implementedInterface in component.GetInterfaces())
            {
                if (HasComponent(implementedInterface))
                {
                    var existingRegistration = GetExistingRegistrationsFor(implementedInterface);

                    container.RegisterCollection(implementedInterface, existingRegistration.Union(new[] { registration }));
                }
                else
                {
                    container.AddRegistration(implementedInterface, registration);
                }
            }

            if (HasComponent(component))
            {
                var existingRegistration = GetExistingRegistrationsFor(component);

                var first = existingRegistration.First();
                if (!first.IsEqualTo(registration))
                {
                    container.RegisterCollection(component, existingRegistration.Union(new[] { registration }));
                }
            }
            else
            {
                container.AddRegistration(component, registration);
            }
        }

        public void Configure<T>(Func<T> componentFactory, DependencyLifecycle dependencyLifecycle)
        {
            EnsureContainerIsConfigurable();

            var funcType = typeof(T);
            var registration = GetRegistrationFromDependencyLifecycle(dependencyLifecycle, funcType, () => componentFactory());

            foreach (var implementedInterface in funcType.GetInterfaces())
            {
                if (HasComponent(implementedInterface))
                {
                    var existingRegistration = GetExistingRegistrationsFor(implementedInterface);

                    container.RegisterCollection(implementedInterface, existingRegistration.Union(new[] { registration }));
                }
                else
                {
                    var interfaceRegistration = GetRegistrationFromDependencyLifecycle(dependencyLifecycle, implementedInterface, () => componentFactory());
                    container.AddRegistration(implementedInterface, interfaceRegistration);
                }
            }

            container.AddRegistration(funcType, registration);
        }

        IEnumerable<Registration> GetExistingRegistrationsFor(Type implementedInterface)
        {
            return container.GetCurrentRegistrations().Where(r => r.ServiceType == implementedInterface).Select(r => r.Registration);
        }

        IEnumerable<Registration> GetExistingRegistrationsFor<TType>()
        {
            return GetExistingRegistrationsFor(typeof(TType));
        }

        private void EnsureContainerIsConfigurable()
        {
            if (isBuilt)
            {
                container = container.Clone();
            }
        }

        public void RegisterSingleton(Type lookupType, object instance)
        {
            EnsureContainerIsConfigurable();

            var registration = GetRegistrationFromDependencyLifecycle(DependencyLifecycle.SingleInstance, lookupType, instance);

            foreach (var implementedInterface in lookupType.GetInterfaces())
            {
                if (HasComponent(implementedInterface))
                {
                    var existingRegistrations = GetExistingRegistrationsFor(implementedInterface);

                    container.RegisterCollection(implementedInterface, existingRegistrations.Union(new[] { registration }));
                }
                else
                {
                    container.AddRegistration(implementedInterface, registration);
                }
            }

            container.Register(lookupType, () => instance, registration.Lifestyle); // urgh
        }

        public bool HasComponent(Type componentType)
        {
            return GetExistingRegistrationsFor(componentType).Any();
        }

        public void Release(object instance)
        {
        }

        Lifestyle GetLifestyleFromDependencyLifecycle(DependencyLifecycle dependencyLifecycle)
        {
            switch (dependencyLifecycle)
            {
                case DependencyLifecycle.SingleInstance:
                    return Lifestyle.Singleton;

                case DependencyLifecycle.InstancePerUnitOfWork:
                    return Lifestyle.Scoped;

                case DependencyLifecycle.InstancePerCall:
                default:
                    return Lifestyle.Transient;
            }
        }

        Registration GetRegistrationFromDependencyLifecycle(DependencyLifecycle dependencyLifecycle, Type component)
        {
            return GetLifestyleFromDependencyLifecycle(dependencyLifecycle).CreateRegistration(component, container);
        }

        Registration GetRegistrationFromDependencyLifecycle(DependencyLifecycle dependencyLifecycle, Type component, Func<object> creator)
        {
            return GetLifestyleFromDependencyLifecycle(dependencyLifecycle).CreateRegistration(component, creator, container);
        }

        Registration GetRegistrationFromDependencyLifecycle(DependencyLifecycle dependencyLifecycle, Type component, object instance)
        {
            return GetRegistrationFromDependencyLifecycle(dependencyLifecycle, component, () => instance);
        }

        static void SetPropertyValue(object instance, string propertyName, object value)
        {
            instance.GetType()
                .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .SetValue(instance, value, null);
        }
    }
}