﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akkatecture.Aggregates;
using Akkatecture.Core;
using Akkatecture.Sagas;
using Akkatecture.Subscribers;

namespace Akkatecture.Extensions
{
    public static class TypeExtensions
    {
        private static readonly ConcurrentDictionary<Type, string> PrettyPrintCache = new ConcurrentDictionary<Type, string>();

        public static string PrettyPrint(this Type type)
        {
            return PrettyPrintCache.GetOrAdd(
                type,
                t =>
                {
                    try
                    {
                        return PrettyPrintRecursive(t, 0);
                    }
                    catch (Exception)
                    {
                        return t.Name;
                    }
                });
        }

        private static string PrettyPrintRecursive(Type type, int depth)
        {
            if (depth > 3)
            {
                return type.Name;
            }

            var nameParts = type.Name.Split('`');
            if (nameParts.Length == 1)
            {
                return nameParts[0];
            }

            var genericArguments = type.GetTypeInfo().GetGenericArguments();
            return !type.IsConstructedGenericType
                ? $"{nameParts[0]}<{new string(',', genericArguments.Length - 1)}>"
                : $"{nameParts[0]}<{string.Join(",", genericArguments.Select(t => PrettyPrintRecursive(t, depth + 1)))}>";
        }

        private static readonly ConcurrentDictionary<Type, AggregateName> AggregateNames = new ConcurrentDictionary<Type, AggregateName>();

        public static AggregateName GetAggregateName(
            this Type aggregateType)
        {
            return AggregateNames.GetOrAdd(
                aggregateType,
                t =>
                {
                    if (!typeof(IAggregateRoot).GetTypeInfo().IsAssignableFrom(aggregateType))
                    {
                        throw new ArgumentException($"Type '{aggregateType.PrettyPrint()}' is not an aggregate root");
                    }

                    return new AggregateName(
                        t.GetTypeInfo().GetCustomAttributes<AggregateNameAttribute>().SingleOrDefault()?.Name ??
                        t.Name);
                });
        }


        private static readonly ConcurrentDictionary<Type, AggregateName> SagaNames = new ConcurrentDictionary<Type, AggregateName>();

        public static AggregateName GetSagaName(
            this Type sagaType)
        {
            return SagaNames.GetOrAdd(
                sagaType,
                t =>
                {
                    if (!typeof(IAggregateRoot).GetTypeInfo().IsAssignableFrom(sagaType))
                    {
                        throw new ArgumentException($"Type '{sagaType.PrettyPrint()}' is not a saga.");
                    }

                    return new AggregateName(
                        t.GetTypeInfo().GetCustomAttributes<SagaNameAttribute>().SingleOrDefault()?.Name ??
                        t.Name);
                });
        }

        internal static IReadOnlyDictionary<Type, Action<T, IAggregateEvent>> GetAggregateEventApplyMethods<TAggregate, TIdentity, T>(this Type type)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            var aggregateEventType = typeof(IAggregateEvent<TAggregate, TIdentity>);

            return type
                .GetTypeInfo()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(mi =>
                {
                    if (mi.Name != "Apply") return false;
                    var parameters = mi.GetParameters();
                    return
                        parameters.Length == 1 &&
                        aggregateEventType.GetTypeInfo().IsAssignableFrom(parameters[0].ParameterType);
                })
                .ToDictionary(
                    mi => mi.GetParameters()[0].ParameterType,
                    mi => ReflectionHelper.CompileMethodInvocation<Action<T, IAggregateEvent>>(type, "Apply", mi.GetParameters()[0].ParameterType));
        }

        internal static IReadOnlyDictionary<Type, Action<TAggregateState, IAggregateEvent>> GetAggregateStateEventApplyMethods<TAggregate, TIdentity, TAggregateState>(this Type type)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
            where TAggregateState : IEventApplier<TAggregate, TIdentity>
        {
            var aggregateEventType = typeof(IAggregateEvent<TAggregate, TIdentity>);

            return type
                .GetTypeInfo()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(mi =>
                {
                    if (mi.Name != "Apply") return false;
                    var parameters = mi.GetParameters();
                    return
                        parameters.Length == 1 &&
                        aggregateEventType.GetTypeInfo().IsAssignableFrom(parameters[0].ParameterType);
                })
                .ToDictionary(
                    mi => mi.GetParameters()[0].ParameterType,
                    mi => ReflectionHelper.CompileMethodInvocation<Action<TAggregateState, IAggregateEvent>>(type, "Apply", mi.GetParameters()[0].ParameterType));
        }

        
        internal static IReadOnlyList<Type> GetDomainEventSubscriberSubscriptionTypes<TAggregate, TIdentity>(this Type type)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            var aggregateEventType = typeof(IAggregateEvent<TAggregate, TIdentity>);

            var interfaces = type
                .GetTypeInfo()
                .GetInterfaces()
                .Select(i => i.GetTypeInfo())
                .ToList();
            var aggregateEventSubscriptionTypes = interfaces
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISubscribeTo<,,>))
                .Where(i => aggregateEventType.GetTypeInfo().IsAssignableFrom(i.GenericTypeArguments[2]))
                .Select(i => i.GetGenericArguments()[2])
                .ToList();
            var domainEventTypes = aggregateEventSubscriptionTypes
                .Select(t => typeof(IDomainEvent<,,>).MakeGenericType(typeof(TAggregate), typeof(TIdentity), t))
                .ToList();

            return domainEventTypes;
        }
        
        internal static IReadOnlyList<Type> GetDomainEventSubscriberSubscriptionTypes(this Type type)
        {
            //TODO
            //Check generic arguments for sanity
            //add checks for iaggregateroot
            //add checks for iidentity
            //add checks for iaggregatevent

            var interfaces = type
                .GetTypeInfo()
                .GetInterfaces()
                .Select(i => i.GetTypeInfo())
                .ToList();
            var domainEventTypes = interfaces
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISubscribeTo<,,>))
                .Select(i =>   typeof(IDomainEvent<,,>).MakeGenericType(i.GetGenericArguments()[0],i.GetGenericArguments()[1],i.GetGenericArguments()[2]))
                .ToList();
            

            return domainEventTypes;
        }

        internal static IReadOnlyList<Type> GetSagaStartEventSubscriptionTypes(this Type type)
        {
            //TODO
            //Check generic arguments for sanity
            //add checks for iaggregateroot
            //add checks for iidentity
            //add checks for iaggregatevent

            var interfaces = type
                .GetTypeInfo()
                .GetInterfaces()
                .Select(i => i.GetTypeInfo())
                .ToList();
            var domainEventTypes = interfaces
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISagaIsStartedBy<,,>))
                .Select(t => typeof(IDomainEvent<,,>).MakeGenericType(t.GetGenericArguments()[0],
                    t.GetGenericArguments()[1], t.GetGenericArguments()[2]))
                .ToList();

            return domainEventTypes;
        }

        internal static IReadOnlyList<Type> GetSagaHandleEventSubscriptionTypes(this Type type)
        {
            //TODO
            //add checks for iaggregateroot
            //add checks for iidentity
            //add checks for iaggregatevent

            var interfaces = type
                .GetTypeInfo()
                .GetInterfaces()
                .Select(i => i.GetTypeInfo())
                .ToList();
            var domainEventTypes = interfaces
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISagaHandles<,,>))
                .Select(t => typeof(IDomainEvent<,,>).MakeGenericType(t.GetGenericArguments()[0],
                    t.GetGenericArguments()[1], t.GetGenericArguments()[2]))
                    .ToList();

            return domainEventTypes;
            
        }

        internal static Type GetBaseType(this Type type, string name)
        {
            var currentType = type;

            while (currentType != null)
            {
                if (currentType.Name.Contains(name))
                {
                    return currentType;
                }
                currentType = currentType.BaseType;
            }
            return type;
        }

    }
}