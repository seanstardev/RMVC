using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RMVC
{
    /// <summary>Owns one facade's immutable gate registrations and exact-type lookup.</summary>
    internal sealed class RCommandLockRegistry
    {
        internal RCommandLockRegistry(RFacade facade)
        {
            this.facade = facade;
        }

        internal bool HasRegistrations
            => gates.Count != 0;

        internal void Register(IRCommandLock[] registrations)
        {
            lock (registrationGate)
            {
                if (registered)
                    throw new InvalidOperationException(
                        "RegisterCommandLocks may be called only once per facade.");

                registered = true;

                if (registrations == null)
                    throw new InvalidOperationException(
                        "RegisterCommandLocks returned null for " + facade.GetType().FullName + ".");

                registrations = (IRCommandLock[])registrations.Clone();

                var proposed = new Dictionary<Type, IRCommandLock>();

                foreach (var entry in registrations)
                {
                    if (entry == null || entry.GetType().IsValueType)
                        throw new InvalidOperationException(
                            "Command locks must be non-null reference-type instances.");

                    var type = entry.GetType();

                    if (proposed.ContainsKey(type))
                        throw new InvalidOperationException(
                            "Duplicate command lock registration: " + type.FullName + ".");

                    if (owners.TryGetValue(entry, out var owner) && ReferenceEquals(owner, facade) == false)
                        throw new InvalidOperationException(
                            "A command lock instance cannot be registered by two facades: " + type.FullName + ".");

                    proposed.Add(type, entry);
                }

                foreach (var entry in proposed)
                {
                    owners.Add(entry.Value, facade);
                    gates.Add(entry.Key, entry.Value);
                }
            }
        }

        internal TLock Resolve<TLock>() where TLock : class, IRCommandLock
        {
            if (gates.TryGetValue(typeof(TLock), out var entry) == false)
                throw new InvalidOperationException(
                    "Facade '" + facade.GetType().FullName + "' has no command lock '"
                        + typeof(TLock).FullName + "'.");

            return (TLock)entry;
        }

        internal IRCommandLock[] Validate(
            RTracker tracker,
            IRCommandLock[] declarations,
            bool progressCallback)
        {
            if (declarations == null)
                throw new InvalidOperationException(
                    "GetLocks returned null for " + tracker.Command.Name + ".");

            if (progressCallback && declarations.Length != 0)
                throw new InvalidOperationException(
                    "Progress callback '" + tracker.Command.Name
                        + "' must not declare command locks; reporting cannot wait behind business work.");

            declarations = (IRCommandLock[])declarations.Clone();

            var unique = new List<IRCommandLock>();

            foreach (var entry in declarations)
            {
                if (entry == null || gates.TryGetValue(entry.GetType(), out var registeredGate) == false
                    || ReferenceEquals(entry, registeredGate) == false)
                {
                    var gateName = entry?.GetType().FullName
                        ?? "null";

                    throw new InvalidOperationException(
                        "GetLocks must return instances registered by facade '"
                            + facade.GetType().FullName + "'; command=" + tracker.Command.Name
                            + "; gate=" + gateName + ".");
                }

                if (unique.Exists(existing => ReferenceEquals(existing, entry)))
                {
                    RCommandLockCoordinator.Report(
                        "COMMAND LOCK DUPLICATE: command=" + tracker.Command.Name
                            + "; gate=" + entry.GetType().FullName + "; reserved once.");

                    continue;
                }

                unique.Add(entry);
            }

            return unique.ToArray();
        }

        private static readonly object registrationGate = new object();

        private static readonly ConditionalWeakTable<IRCommandLock, RFacade> owners =
            new ConditionalWeakTable<IRCommandLock, RFacade>();

        private readonly RFacade facade;

        private readonly Dictionary<Type, IRCommandLock> gates = new Dictionary<Type, IRCommandLock>();

        private bool registered;
    }
}
