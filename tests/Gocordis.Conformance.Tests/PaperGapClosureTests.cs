using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Executable closure of the paper items that were previously only described in
/// docs/migration/paper-coverage-matrix.md. Every assertion observes behavior
/// reachable from the public Component / Context / Runtime / PluginRegistry /
/// Extensions surface; no Core private state, file names or documents are read.
/// </summary>
public sealed class PaperGapClosureTests
{
    [Fact]
    public async Task C01_TemporalRecoveryAndSpatialResolutionAreOneContract()
    {
        // Time dimension: an installed effect is inverted on withdrawal.
        // Space dimension: a declaration made before its provider is gated,
        // then activated reactively when the provider appears.
        var key = new Key<int>("c01-space");
        var effectLog = new List<string>();
        var observed = 0;

        await using var runtime = new Runtime();
        var consumerFiber = await runtime.LoadAsync(new Step(
            "consumer",
            context =>
            {
                observed = context.Require(key);
                return Task.CompletedTask;
            },
            inject: [key]));

        Assert.Equal(FiberState.Pending, consumerFiber.State);
        Assert.Equal(0, observed);

        var providerFiber = await runtime.LoadAsync(new Step(
            "provider",
            async context =>
            {
                context.Provide(key, 7);
                await context.EffectAsync(
                    () =>
                    {
                        effectLog.Add("install");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        effectLog.Add("undo");
                        return ValueTask.CompletedTask;
                    });
                return null;
            },
            provide: [key]));

        Assert.Equal(FiberState.Active, consumerFiber.State);
        Assert.Equal(7, observed);
        Assert.Equal(new[] { "install" }, effectLog);

        await runtime.DisposeAsync(providerFiber);

        Assert.Equal(new[] { "install", "undo" }, effectLog);
        Assert.Equal(FiberState.Pending, consumerFiber.State);
        Assert.Null(consumerFiber.Failure);
    }

    [Fact]
    public async Task C02_EffectInverseIsLeftInverseNotRightInverse()
    {
        // f : [] -> [e], g : [e] -> []. g∘f = id on the installed state,
        // while f∘g is not id on the recovered state.
        var trace = new List<string>();
        Func<ValueTask> forward = () =>
        {
            trace.Add("e");
            return ValueTask.CompletedTask;
        };

        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new Step(
            "effect",
            async context =>
            {
                await context.EffectAsync(
                    forward,
                    () =>
                    {
                        trace.RemoveAt(trace.Count - 1);
                        return ValueTask.CompletedTask;
                    });
                return null;
            }));

        Assert.Equal(new[] { "e" }, trace);
        await runtime.DisposeAsync(fiber);
        Assert.Empty(trace); // (g ∘ f)(γ0) = γ0

        await forward();
        Assert.Equal(new[] { "e" }, trace); // (f ∘ g)(γ0) != γ0
        Assert.Equal(FiberState.Gone, fiber.State);
    }

    [Fact]
    public async Task C04_EffectCompositionHasIdentityAndReverseOrderInverse()
    {
        var trace = new List<string>();
        EffectRegistration[] Effects() =>
        [
            Registration(1),
            Registration(2),
            Registration(3),
        ];

        EffectRegistration Registration(int index) => EffectRegistration.From(
            () =>
            {
                trace.Add($"f{index}");
                return ValueTask.CompletedTask;
            },
            () =>
            {
                trace.Add($"g{index}");
                return ValueTask.CompletedTask;
            });

        static async IAsyncEnumerable<EffectRegistration> Sequence(EffectRegistration[] effects)
        {
            foreach (var effect in effects)
            {
                yield return effect;
            }

            await ValueTask.CompletedTask;
        }

        static async IAsyncEnumerable<EffectRegistration> LeftAssociated(EffectRegistration[] effects)
        {
            await foreach (var effect in Sequence([.. effects[..2], effects[2]]))
            {
                yield return effect;
            }
        }

        static async IAsyncEnumerable<EffectRegistration> RightAssociated(EffectRegistration[] effects)
        {
            await foreach (var effect in Sequence([effects[0], .. effects[1..]]))
            {
                yield return effect;
            }
        }

        static async IAsyncEnumerable<EffectRegistration> Empty()
        {
            await ValueTask.CompletedTask;
            yield break;
        }

        await using var runtime = new Runtime();
        var identity = await runtime.LoadAsync(new Steps("identity", _ => Empty()));
        await runtime.DisposeAsync(identity);
        Assert.Empty(trace); // ε is the identity of the effect monoid

        var left = await runtime.LoadAsync(new Steps("left", _ => LeftAssociated(Effects())));
        var leftTrace = trace.ToArray();
        await runtime.DisposeAsync(left);
        var leftFull = trace.ToArray();

        trace.Clear();
        var right = await runtime.LoadAsync(new Steps("right", _ => RightAssociated(Effects())));
        var rightTrace = trace.ToArray();
        await runtime.DisposeAsync(right);
        var rightFull = trace.ToArray();

        // Associative grouping installs the same effects; inverses compose in
        // reverse application order (g3 ∘ g2 ∘ g1), never in install order.
        Assert.Equal(leftTrace, rightTrace);
        Assert.Equal(new[] { "f1", "f2", "f3" }, rightTrace);
        Assert.Equal(leftFull, rightFull);
        Assert.Equal(new[] { "f1", "f2", "f3", "g3", "g2", "g1" }, rightFull);
    }

    [Fact]
    public async Task C05_WitnessEffectMovesDeltaToGammaAndBack()
    {
        var state = "delta";
        var observed = new List<string>();

        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new Step(
            "witness",
            async context =>
            {
                await context.EffectAsync(
                    () =>
                    {
                        state = "gamma";
                        observed.Add(state);
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        state = "delta";
                        observed.Add(state);
                        return ValueTask.CompletedTask;
                    });
                return null;
            }));

        Assert.Equal("gamma", state);
        Assert.Equal(FiberState.Active, fiber.State);

        await runtime.DisposeAsync(fiber);

        Assert.Equal("delta", state);
        Assert.Equal(new[] { "gamma", "delta" }, observed);
        Assert.Equal(FiberState.Gone, fiber.State);
    }

    [Fact]
    public async Task C07_CoeffectGetSetPreconditionsAndReversibleProvision()
    {
        var secret = new Key<int>("c07-secret");
        var ghost = new Key<int>("c07-ghost");
        var dup = new Key<int>("c07-dup");
        var foreign = new Key<int>("c07-foreign");
        Exception? undeclaredGet = null;
        Exception? undeclaredSet = null;
        Exception? duplicateSet = null;
        var ghostApplies = 0;

        await using var runtime = new Runtime();
        await runtime.LoadAsync(new Step(
            "secret-provider",
            context =>
            {
                context.Provide(secret, 1);
                return Task.CompletedTask;
            },
            provide: [secret]));
        await runtime.LoadAsync(new Step(
            "foreign-provider",
            context =>
            {
                context.Provide(foreign, 2);
                return Task.CompletedTask;
            },
            provide: [foreign]));

        var gated = await runtime.LoadAsync(new Step(
            "gated-consumer",
            _ =>
            {
                ghostApplies++;
                return Task.CompletedTask;
            },
            inject: [ghost]));
        Assert.Equal(FiberState.Pending, gated.State); // get requires k to exist
        Assert.Equal(0, ghostApplies);

        var intruder = await runtime.LoadAsync(new Step(
            "intruder",
            context =>
            {
                undeclaredGet = Capture(() => context.Require(secret));
                undeclaredSet = Capture(() => context.Provide(foreign, 99));
                return Task.CompletedTask;
            }));
        Assert.IsType<UndeclaredCapabilityException>(undeclaredGet);
        Assert.IsType<UndeclaredCapabilityException>(undeclaredSet);
        Assert.Equal(FiberState.Active, intruder.State);

        var duplicating = await runtime.LoadAsync(new Step(
            "duplicating",
            context =>
            {
                context.Provide(dup, 1);
                duplicateSet = Capture(() => context.Provide(dup, 2));
                return Task.CompletedTask;
            },
            provide: [dup]));
        Assert.IsType<DuplicateProviderException>(duplicateSet);
        Assert.Equal(FiberState.Active, duplicating.State);

        await runtime.DisposeAsync(duplicating); // provision is a reversible effect
        var replacement = await runtime.LoadAsync(new Step(
            "replacement",
            context =>
            {
                context.Provide(dup, 3);
                return Task.CompletedTask;
            },
            provide: [dup]));
        Assert.Equal(FiberState.Active, replacement.State);
    }

    [Fact]
    public async Task C08_DeclarationGuardHasThreeObservableNotificationPhases()
    {
        // notify_d's activating / deactivating / neutral are observable through
        // FiberState and the Context read gate; an unbound key is never read.
        var key = new Key<int>("c08-bound");
        var foreign = new Key<int>("c08-foreign");
        var seen = new List<string>();
        Exception? unbound = null;
        Fiber? handle = null;

        await using var runtime = new Runtime();
        await runtime.LoadAsync(new Step(
            "foreign",
            context =>
            {
                context.Provide(foreign, 5);
                return Task.CompletedTask;
            },
            provide: [foreign]));

        var consumer = new Step(
            "consumer",
            async context =>
            {
                seen.Add($"apply:{context.Require(key)}");
                unbound = Capture(() => context.Require(foreign));
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        seen.Add($"cleanup:{handle!.State}:{context.Require(key)}");
                        return ValueTask.CompletedTask;
                    });
                return null;
            },
            inject: [key]);

        var pending = await runtime.LoadAsync(consumer);
        Assert.Equal(FiberState.Pending, pending.State); // activating not entered
        Assert.Empty(seen);

        var provider = await runtime.LoadAsync(new Step(
            "provider",
            context =>
            {
                context.Provide(key, 11);
                return Task.CompletedTask;
            },
            provide: [key]));
        var fiber = runtime.Snapshot().Fibers.Single(f => f.Name == "consumer");
        handle = runtime.GetFiber(fiber.Id);
        Assert.NotNull(handle);
        Assert.Equal(FiberState.Active, handle.State); // neutral

        await runtime.DisposeAsync(handle);

        Assert.IsType<UndeclaredCapabilityException>(unbound);
        Assert.Equal(new[] { "apply:11", "cleanup:Unloading:11" }, seen);
        Assert.Equal(FiberState.Gone, handle.State);
        Assert.Equal(FiberState.Active, provider.State); // deactivation did not retire the dependency
    }

    [Fact]
    public async Task C09_InSituProvisionAndDerivedIsolationBothRestoreOnUnload()
    {
        var key = new Key<string>("c09-route");
        var scoped = new Realm("c09-scoped");
        var seen = new List<string>();

        await using var runtime = new Runtime();
        var defaultProvider = await runtime.LoadAsync(new Step(
            "default-provider",
            context =>
            {
                context.Provide(key, "D");
                return Task.CompletedTask;
            },
            provide: [key]));
        var scopedProvider = await runtime.LoadAsync(new Step(
            "scoped-provider",
            context =>
            {
                context.Provide(key, "S");
                return Task.CompletedTask;
            },
            provide: [key]), realm: scoped);

        // Derived implementation: the consumer switches to a derived view of Σ;
        // its inverse is just dropping that view, so a later consumer sees the
        // original route again.
        var derived = await runtime.LoadAsync(new Step(
            "derived-consumer",
            context =>
            {
                seen.Add(context.Require(key));
                context.Isolate(key, scoped);
                seen.Add(context.Require(key));
                return Task.CompletedTask;
            },
            inject: [key]));
        await runtime.DisposeAsync(derived);

        await runtime.LoadAsync(new Step(
            "plain-consumer",
            context =>
            {
                seen.Add(context.Require(key));
                return Task.CompletedTask;
            },
            inject: [key]));
        Assert.Equal(new[] { "D", "S", "D" }, seen);

        // In-situ implementation: an own-realm consumer is gated by the scoped
        // provider and its nontrivial inverse is that provider's withdrawal.
        var scopedConsumer = await runtime.LoadAsync(new Step(
            "scoped-consumer",
            context =>
            {
                seen.Add(context.Require(key));
                return Task.CompletedTask;
            },
            inject: [key]), realm: scoped);
        await runtime.DisposeAsync(scopedProvider);
        Assert.Equal(FiberState.Pending, scopedConsumer.State);

        await runtime.LoadAsync(new Step(
            "scoped-provider-2",
            context =>
            {
                context.Provide(key, "S2");
                return Task.CompletedTask;
            },
            provide: [key]), realm: scoped);
        Assert.Equal(FiberState.Active, scopedConsumer.State);
        Assert.Equal(new[] { "D", "S", "D", "S", "S2" }, seen);
        Assert.Equal(FiberState.Active, defaultProvider.State);
    }

    [Fact]
    public async Task C11_MetaInterceptionIsRightBiasedAndDoesNotReload()
    {
        var tag = new MetaKey<int, string>("c11-tag", string.Empty, (declared, overridden) => declared + overridden);
        var observed = new List<string>();

        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new Step(
            "meta-consumer",
            context =>
            {
                context.DeclareMeta(tag, "D");
                observed.Add(context.Meta(tag));
                context.OverrideMeta(tag, "O");
                observed.Add(context.Meta(tag));
                return Task.CompletedTask;
            },
            inject: [tag]));

        Assert.Equal(new[] { "D", "DO" }, observed);
        Assert.DoesNotContain("OD", observed); // override is always applied last
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal(1, fiber.Generation); // interception did not reload the fiber

        var fresh = await runtime.LoadAsync(new Step(
            "fresh-consumer",
            context =>
            {
                observed.Add(context.Meta(tag));
                return Task.CompletedTask;
            },
            inject: [tag]));
        Assert.Equal(new[] { "D", "DO", "" }, observed); // no cross-root leakage
        await runtime.DisposeAsync(fiber);
        Assert.Equal(FiberState.Active, fresh.State);
    }

    [Fact]
    public async Task C12_OneContextMediatesCapabilitiesEffectsMetaDataEventsAndChildren()
    {
        var input = new Key<int>("c12-in");
        var output = new Key<int>("c12-out");
        var notice = new EventKey<string>("c12-event");
        var tag = new MetaKey<int, string>("c12-meta", string.Empty, (declared, overridden) => declared + overridden);
        var effectLog = new List<string>();
        var listenerReceived = new List<string>();
        var ownReceived = new List<string>();
        var metaSeen = string.Empty;
        Fiber? child = null;
        var outputSeen = 0;

        await using var runtime = new Runtime();
        await runtime.LoadAsync(new Step(
            "input",
            context =>
            {
                context.Provide(input, 1);
                return Task.CompletedTask;
            },
            provide: [input]));
        await runtime.LoadAsync(new Step(
            "channel",
            context =>
            {
                context.Provide(notice, "channel");
                return Task.CompletedTask;
            },
            provide: [notice]));
        await runtime.LoadAsync(new Step(
            "listener",
            async context =>
            {
                await context.OnAsync(notice, payload =>
                {
                    listenerReceived.Add(payload);
                    return ValueTask.CompletedTask;
                });
                return null;
            },
            inject: [notice]));

        var mediator = await runtime.LoadAsync(new Step(
            "mediator",
            async context =>
            {
                var value = context.Require(input);
                context.Provide(output, value + 1);
                await context.EffectAsync(
                    () =>
                    {
                        effectLog.Add("install");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        effectLog.Add("undo");
                        return ValueTask.CompletedTask;
                    });
                context.DeclareMeta(tag, "decl");
                context.OverrideMeta(tag, "over");
                metaSeen = context.Meta(tag);
                await context.OnAsync(notice, payload =>
                {
                    ownReceived.Add(payload);
                    return ValueTask.CompletedTask;
                });
                await context.EmitAsync(notice, "ping");
                child = await context.LoadChildAsync(new Step("child", _ => Task.CompletedTask));
                return null;
            },
            inject: [input, notice, tag],
            provide: [output]));

        await runtime.LoadAsync(new Step(
            "output-consumer",
            context =>
            {
                outputSeen = context.Require(output);
                return Task.CompletedTask;
            },
            inject: [output]));

        Assert.Equal(2, outputSeen);
        Assert.Equal("declover", metaSeen);
        Assert.Equal(new[] { "ping" }, listenerReceived);
        Assert.Equal(new[] { "ping" }, ownReceived);
        Assert.NotNull(child);
        Assert.Equal(mediator.Id, child.Parent!.Id);
        Assert.Contains(child.Id, runtime.Snapshot().Fibers.Select(fiber => fiber.Id));

        await runtime.DisposeAsync(mediator);
        Assert.Equal(new[] { "install", "undo" }, effectLog);
        Assert.Equal(FiberState.Gone, child.State);
    }

    [Fact]
    public async Task C14_IndependentInversesCommuteAcrossPermittedOrders()
    {
        static async Task<string[]> Run(string order)
        {
            var log = new List<string>();
            await using var runtime = new Runtime();

            async Task<Fiber> Load(string name, string token) => await runtime.LoadAsync(new Step(
                name,
                async context =>
                {
                    await context.EffectAsync(
                        () =>
                        {
                            log.Add($"{token}+");
                            return ValueTask.CompletedTask;
                        },
                        () =>
                        {
                            log.Add($"{token}-");
                            return ValueTask.CompletedTask;
                        });
                    return null;
                }));

            var first = await Load("a", "a");
            var second = await Load("b", "b");
            if (order == "ab")
            {
                await runtime.DisposeAsync(first);
                await runtime.DisposeAsync(second);
            }
            else
            {
                await runtime.DisposeAsync(second);
                await runtime.DisposeAsync(first);
            }

            Assert.Equal(FiberState.Gone, first.State);
            Assert.Equal(FiberState.Gone, second.State);
            Assert.Empty(runtime.Snapshot().Fibers);
            return log.ToArray();
        }

        var forward = await Run("ab");
        var reversed = await Run("ba");

        // The runtime applies inverses in LIFO order within a fiber; across two
        // independent fibers, the permitted orderings are exactly these two.
        Assert.Equal(new[] { "a+", "b+", "a-", "b-" }, forward);
        Assert.Equal(new[] { "a+", "b+", "b-", "a-" }, reversed);
        Assert.Equal(
            forward.Where(entry => entry.EndsWith("-", StringComparison.Ordinal)).OrderBy(entry => entry, StringComparer.Ordinal),
            reversed.Where(entry => entry.EndsWith("-", StringComparison.Ordinal)).OrderBy(entry => entry, StringComparer.Ordinal));
    }

    [Fact]
    public async Task C15_DeclaredComponentSurfaceGatesAndMatchesTheRuntimeView()
    {
        var required = new Key<int>("c15-required");
        var provided = new Key<int>("c15-provided");
        var applies = 0;
        var seen = 0;

        await using var runtime = new Runtime();
        var consumer = new Step(
            "consumer",
            context =>
            {
                applies++;
                seen = context.Require(required);
                return Task.CompletedTask;
            },
            inject: [required],
            provide: [provided]);

        var pending = await runtime.LoadAsync(consumer);
        var pendingSnapshot = SnapshotOf(runtime, "consumer");
        Assert.Equal(FiberState.Pending, pending.State);
        Assert.Equal(new[] { required.Name }, pendingSnapshot.Requires);
        Assert.Equal(new[] { provided.Name }, pendingSnapshot.Provides);
        Assert.Equal(0, applies);

        await runtime.LoadAsync(new Step(
            "provider",
            context =>
            {
                context.Provide(required, 31);
                return Task.CompletedTask;
            },
            provide: [required]));

        Assert.Equal(FiberState.Active, pending.State);
        Assert.Equal(1, applies);
        Assert.Equal(31, seen);
        var activeSnapshot = SnapshotOf(runtime, "consumer");
        Assert.Equal(new[] { required.Name }, activeSnapshot.Requires);
        Assert.Equal(new[] { provided.Name }, activeSnapshot.Provides);
        Assert.Contains($"{required.Name}->", activeSnapshot.BoundProviders.Single());
    }

    [Fact]
    public async Task C16_FiberStatesAndCommittedProviderViewAreObservable()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<FiberState>();
        Fiber? handle = null;

        await using var runtime = new Runtime();
        var component = new Step(
            "observed",
            async context =>
            {
                started.TrySetResult();
                await releaseApply.Task;
                return async () =>
                {
                    if (handle is not null)
                    {
                        observed.Add(handle.State);
                    }

                    enteredCleanup.TrySetResult();
                    await releaseCleanup.Task;
                };
            });

        var loading = runtime.LoadAsync(component);
        await started.Task;
        handle = runtime.GetFiber(SnapshotOf(runtime, "observed").Id);
        Assert.NotNull(handle);
        observed.Add(handle.State); // Reloading
        Assert.Empty(SnapshotOf(runtime, "observed").BoundProviders); // ω not committed yet

        releaseApply.TrySetResult();
        var fiber = await loading;
        observed.Add(fiber.State); // Active
        Assert.Equal(FiberState.Active, handle.State);

        var disposing = runtime.DisposeAsync(fiber);
        await enteredCleanup.Task;
        Assert.Equal(FiberState.Unloading, handle.State);
        releaseCleanup.TrySetResult();
        await disposing;
        observed.Add(handle.State); // Inactive/Gone

        Assert.Equal(
            new[] { FiberState.Loading, FiberState.Active, FiberState.Unloading, FiberState.Gone },
            observed);

        var missing = new Key<int>("c16-missing");
        var inactive = await runtime.LoadAsync(new Step(
            "inactive",
            _ => Task.CompletedTask,
            inject: [missing]));
        Assert.Equal(FiberState.Pending, inactive.State); // Inactive

        var failed = await runtime.LoadAsync(new Step(
            "failed",
            (Func<Context, Task>)(_ => throw new InvalidOperationException("raise"))));
        Assert.Equal(FiberState.Failed, failed.State); // Failed outcome
        Assert.IsType<InvalidOperationException>(failed.Failure);
    }

    [Fact]
    public async Task C17_UniqueProviderAndBrokerBasedMultiImplementation()
    {
        var key = new Key<int>("c17-exclusive");
        await using var runtime = new Runtime();

        var first = await runtime.LoadAsync(new Step(
            "first",
            context =>
            {
                context.Provide(key, 1);
                return Task.CompletedTask;
            },
            provide: [key]));

        var duplicate = await Assert.ThrowsAsync<DuplicateProviderException>(() => runtime.LoadAsync(new Step(
            "second",
            context =>
            {
                context.Provide(key, 2);
                return Task.CompletedTask;
            },
            provide: [key])));
        Assert.Contains(key.Name, duplicate.Message, StringComparison.Ordinal);

        await runtime.DisposeAsync(first);
        var replacement = await runtime.LoadAsync(new Step(
            "replacement",
            context =>
            {
                context.Provide(key, 3);
                return Task.CompletedTask;
            },
            provide: [key]));
        Assert.Equal(FiberState.Active, replacement.State);

        var brokerKey = new Key<Broker<string>>("c17-broker");
        var broker = new Broker<string>();
        await runtime.LoadAsync(new Step(
            "broker-owner",
            context =>
            {
                context.Provide(brokerKey, broker);
                return Task.CompletedTask;
            },
            provide: [brokerKey]));

        async Task<Fiber> Register(string name)
        {
            var route = broker.Register(name, name.ToUpperInvariant());
            return await runtime.LoadAsync(new Step(
                $"backend-{name}",
                async context =>
                {
                    await context.EffectAsync(
                        () => ValueTask.CompletedTask,
                        () =>
                        {
                            route.Dispose();
                            return ValueTask.CompletedTask;
                        });
                    return null;
                },
                inject: [brokerKey]));
        }

        var alpha = await Register("alpha");
        var beta = await Register("beta");
        Assert.Equal(new[] { "alpha", "beta" }, broker.Names());
        Assert.Equal(new[] { "ALPHA", "BETA", "ALPHA" }, new[] { broker.RoundRobin(), broker.RoundRobin(), broker.RoundRobin() });

        await runtime.DisposeAsync(alpha);
        Assert.Equal(new[] { "beta" }, broker.Names());
        Assert.Equal("BETA", broker.First());
        await runtime.DisposeAsync(beta);
        Assert.Empty(broker.Names());
        Assert.Throws<BrokerNoProviderException>(() => broker.First());
    }

    [Fact]
    public async Task C18_InstantiationRetiresChildrenBeforeTheirOwner()
    {
        static async Task<string[]> Run(bool retireChildFirst)
        {
            var order = new List<string>();
            await using var runtime = new Runtime();

            async IAsyncEnumerable<EffectRegistration> Steps(Context context)
            {
                var child = await context.LoadChildAsync(new Step(
                    "child",
                    async inner =>
                    {
                        await inner.EffectAsync(
                            () =>
                            {
                                order.Add("child-install");
                                return ValueTask.CompletedTask;
                            },
                            () =>
                            {
                                order.Add("child-cleanup");
                                return ValueTask.CompletedTask;
                            });
                        return null;
                    }));
                order.Add($"child-parent:{ReferenceEquals(child.Parent, null)}");
                yield return EffectRegistration.From(
                    () =>
                    {
                        order.Add("parent-install");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        order.Add("parent-cleanup");
                        return ValueTask.CompletedTask;
                    });
            }

            var parent = await runtime.LoadAsync(new Steps("parent", Steps));
            var childFiber = Assert.Single(parent.Children);
            Assert.Equal(parent.Id, childFiber.Parent!.Id);

            // LoadChildAsync intentionally does not wait for the child's
            // activation. Wait for its install step so the teardown-order
            // assertions below are not racing a still-running child Apply.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!order.Contains("child-install") && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.Contains("child-install", order);

            if (retireChildFirst)
            {
                await runtime.DisposeAsync(childFiber);
                Assert.Equal(FiberState.Gone, childFiber.State);
                Assert.Equal(FiberState.Active, parent.State);
                Assert.DoesNotContain("parent-cleanup", order);
            }

            await runtime.DisposeAsync(parent);
            await childFiber.GoneAsync();
            Assert.Equal(FiberState.Gone, parent.State);
            return order.ToArray();
        }

        var childFirst = await Run(retireChildFirst: true);
        var ownerFirst = await Run(retireChildFirst: false);

        // LoadChildAsync does not order child Apply against parent steps; only
        // ownership and inverse order are part of the paper contract (C18).
        Assert.Contains("child-parent:False", childFirst);
        Assert.Contains("child-install", childFirst);
        Assert.Contains("parent-install", childFirst);
        Assert.Equal(new[] { "child-cleanup", "parent-cleanup" }, childFirst[^2..]);
        Assert.Equal(new[] { "child-cleanup", "parent-cleanup" }, ownerFirst[^2..]);
    }

    [Fact]
    public async Task C20_TargetBindingAndQuiescenceAreObservable()
    {
        static string Signature(Runtime runtime) => string.Join("|", runtime.Snapshot().Fibers
            .OrderBy(fiber => fiber.Name, StringComparer.Ordinal)
            .Select(fiber => $"{fiber.Name}:{fiber.State}:{string.Join(",", fiber.BoundProviders.OrderBy(entry => entry, StringComparer.Ordinal))}"));

        var key = new Key<int>("c20-target");
        var applies = 0;

        await using var runtime = new Runtime();
        var consumer = await runtime.LoadAsync(new Step(
            "consumer",
            context =>
            {
                applies++;
                context.Require(key);
                return Task.CompletedTask;
            },
            inject: [key]));

        Assert.Equal(FiberState.Pending, consumer.State); // target is ⊥
        var quietWithoutProvider = Signature(runtime);
        await Task.Delay(20);
        Assert.Equal(quietWithoutProvider, Signature(runtime));

        var provider = await runtime.LoadAsync(new Step(
            "provider",
            context =>
            {
                context.Provide(key, 4);
                return Task.CompletedTask;
            },
            provide: [key]));

        Assert.Equal(FiberState.Active, consumer.State);
        Assert.Equal(1, applies);
        Assert.Contains($"{key.Name}->{provider.Id}", SnapshotOf(runtime, "consumer").BoundProviders.Single());
        var quietWithProvider = Signature(runtime);
        await Task.Delay(20);
        Assert.Equal(quietWithProvider, Signature(runtime));

        await runtime.DisposeAsync(provider);
        Assert.Equal(FiberState.Pending, consumer.State);
        var quietAfterWithdrawal = Signature(runtime);
        await Task.Delay(20);
        Assert.Equal(quietAfterWithdrawal, Signature(runtime));
        Assert.DoesNotContain(runtime.Snapshot().Fibers, fiber => fiber.State is FiberState.Loading or FiberState.Unloading);
    }

    public static IEnumerable<object[]> C21Scenarios()
    {
        yield return new object[] { "finish" };
        yield return new object[] { "divert" };
        yield return new object[] { "leave-rebegin" };
    }

    [Theory]
    [MemberData(nameof(C21Scenarios))]
    public async Task C21_LifecycleRulesAreObservableForIterativeActivation(string scenario)
    {
        await using var runtime = new Runtime();

        switch (scenario)
        {
            case "finish":
            {
                var log = new List<string>();
                var fiber = await runtime.LoadAsync(new Steps("finish", _ => ThreeSteps(log)));
                Assert.Equal(FiberState.Active, fiber.State);
                Assert.Equal(new[] { "install:1", "install:2", "install:3" }, log);
                await runtime.DisposeAsync(fiber);
                Assert.Equal(
                    new[] { "install:1", "install:2", "install:3", "undo:3", "undo:2", "undo:1" },
                    log);
                break;
            }

            case "divert":
            {
                var log = new List<string>();
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var loading = runtime.LoadAsync(new Steps("divert", context => DivertSteps(context, log, started)));
                await started.Task;
                var handle = runtime.GetFiber(SnapshotOf(runtime, "divert").Id)!;
                var disposing = runtime.DisposeAsync(handle);
                var fiber = await loading;
                await disposing;
                Assert.Equal(FiberState.Gone, fiber.State);
                Assert.Null(fiber.Failure);
                Assert.Equal(new[] { "install:1", "install:2", "install:3", "undo:2", "undo:1" }, log);
                break;
            }

            case "leave-rebegin":
            {
                var key = new Key<int>("c21-dependency");
                var log = new List<string>();
                var applies = 0;
                var consumer = await runtime.LoadAsync(new Steps(
                    "leave-rebegin",
                    context => DependencySteps(context, key, log, () => applies++),
                    inject: [key]));
                Assert.Equal(FiberState.Pending, consumer.State); // L-Begin guard

                var provider = await runtime.LoadAsync(new Step(
                    "provider",
                    context =>
                    {
                        context.Provide(key, 1);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                Assert.Equal(FiberState.Active, consumer.State);
                Assert.Equal(1, applies);

                await runtime.DisposeAsync(provider); // L-Leave
                Assert.Equal(FiberState.Pending, consumer.State);
                Assert.Null(consumer.Failure);
                Assert.Contains("undo:1", log);

                await runtime.LoadAsync(new Step(
                    "provider-2",
                    context =>
                    {
                        context.Provide(key, 2);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                Assert.Equal(FiberState.Active, consumer.State); // L-Begin again
                Assert.Equal(2, applies);
                break;
            }
        }

        static async IAsyncEnumerable<EffectRegistration> ThreeSteps(List<string> log)
        {
            for (var index = 1; index <= 3; index++)
            {
                var current = index;
                yield return EffectRegistration.From(
                    () =>
                    {
                        log.Add($"install:{current}");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        log.Add($"undo:{current}");
                        return ValueTask.CompletedTask;
                    });
                await Task.Yield();
            }
        }

        static async IAsyncEnumerable<EffectRegistration> DivertSteps(
            Context context, List<string> log, TaskCompletionSource started)
        {
            for (var index = 1; index <= 2; index++)
            {
                var current = index;
                yield return EffectRegistration.From(
                    () =>
                    {
                        log.Add($"install:{current}");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        log.Add($"undo:{current}");
                        return ValueTask.CompletedTask;
                    });
            }

            yield return EffectRegistration.From(
                async () =>
                {
                    log.Add("install:3");
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, context.CancellationToken);
                },
                () =>
                {
                    log.Add("undo:3");
                    return ValueTask.CompletedTask;
                });
        }

        static async IAsyncEnumerable<EffectRegistration> DependencySteps(
            Context context, Key<int> key, List<string> log, Action onApply)
        {
            context.Require(key);
            onApply();
            await Task.Yield();
            yield return EffectRegistration.From(
                () => ValueTask.CompletedTask,
                () =>
                {
                    log.Add("undo:1");
                    return ValueTask.CompletedTask;
                });
        }
    }

    [Fact]
    public async Task C23_EffectsMayOnlyWriteOwnStateAndDeclaredProviderSlots()
    {
        var owned = new Key<int>("c23-owned");
        var foreign = new Key<int>("c23-foreign");
        Exception? forbiddenWrite = null;
        var foreignSeen = 0;

        await using var runtime = new Runtime();
        var owner = await runtime.LoadAsync(new Step(
            "owner",
            context =>
            {
                context.Provide(owned, 1);
                forbiddenWrite = Capture(() => context.Provide(foreign, -1));
                return Task.CompletedTask;
            },
            provide: [owned]));
        await runtime.LoadAsync(new Step(
            "foreign-owner",
            context =>
            {
                context.Provide(foreign, 2);
                return Task.CompletedTask;
            },
            provide: [foreign]));
        await runtime.LoadAsync(new Step(
            "foreign-consumer",
            context =>
            {
                foreignSeen = context.Require(foreign);
                return Task.CompletedTask;
            },
            inject: [foreign]));

        Assert.IsType<UndeclaredCapabilityException>(forbiddenWrite);
        Assert.Equal(2, foreignSeen);
        var ownerSnapshot = SnapshotOf(runtime, "owner");
        Assert.Equal(new[] { owned.Name }, ownerSnapshot.Provides);
        Assert.Empty(ownerSnapshot.Requires);
        Assert.Empty(ownerSnapshot.BoundProviders);
        Assert.Equal(FiberState.Active, owner.State);

        await runtime.DisposeAsync(owner);
        Assert.DoesNotContain(runtime.Snapshot().Fibers, fiber => fiber.Name == "owner");
    }

    [Fact]
    public async Task C28_IterativeStepsShareOneResolutionAndRecoverOnDivert()
    {
        var key = new Key<int>("c28-resolution");
        var resolutions = new List<string>();
        var log = new List<string>();

        await using var runtime = new Runtime();
        var provider = await runtime.LoadAsync(new Step(
            "provider",
            context =>
            {
                context.Provide(key, 17);
                return Task.CompletedTask;
            },
            provide: [key]));

        static async IAsyncEnumerable<EffectRegistration> StableSteps(
            Context context, Key<int> key, List<string> resolutions)
        {
            for (var index = 0; index < 3; index++)
            {
                resolutions.Add($"{context.Require(key)}");
                await Task.Yield();
                yield return EffectRegistration.From(
                    () => ValueTask.CompletedTask,
                    () => ValueTask.CompletedTask);
            }
        }

        var stable = await runtime.LoadAsync(new Steps(
            "stable", context => StableSteps(context, key, resolutions), inject: [key]));
        Assert.Equal(FiberState.Active, stable.State);
        Assert.Equal(new[] { "17", "17", "17" }, resolutions);
        Assert.Contains($"{key.Name}->{provider.Id}", SnapshotOf(runtime, "stable").BoundProviders.Single());

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        static async IAsyncEnumerable<EffectRegistration> DivertSteps(
            Context context, Key<int> key, List<string> log, TaskCompletionSource started)
        {
            yield return EffectRegistration.From(
                () =>
                {
                    log.Add("install:1");
                    return ValueTask.CompletedTask;
                },
                () =>
                {
                    log.Add("undo:1");
                    return ValueTask.CompletedTask;
                });
            yield return EffectRegistration.From(
                async () =>
                {
                    log.Add("install:2:" + context.Require(key));
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, context.CancellationToken);
                },
                () =>
                {
                    log.Add("undo:2");
                    return ValueTask.CompletedTask;
                });
        }

        var loading = runtime.LoadAsync(new Steps(
            "divert-resolution", context => DivertSteps(context, key, log, started), inject: [key]));
        await started.Task;
        var handle = runtime.GetFiber(SnapshotOf(runtime, "divert-resolution").Id)!;
        var disposing = runtime.DisposeAsync(handle);
        var diverted = await loading;
        await disposing;

        Assert.Equal(FiberState.Gone, diverted.State);
        Assert.Null(diverted.Failure);
        Assert.Equal(new[] { "install:1", "install:2:17", "undo:1" }, log);
    }

    [Fact]
    public async Task C31_WritePartitionIsInvariantUnderRenamingAndVestigialRecords()
    {
        static async Task<RuntimeSnapshot> Build(string consumerName, string providerName, Key<int> key)
        {
            var runtime = new Runtime();
            await runtime.LoadAsync(new Step(
                consumerName,
                context =>
                {
                    context.Require(key);
                    return Task.CompletedTask;
                },
                inject: [key]));
            await runtime.LoadAsync(new Step(
                providerName,
                context =>
                {
                    context.Provide(key, 1);
                    return Task.CompletedTask;
                },
                provide: [key]));
            return runtime.Snapshot();
        }

        var key = new Key<int>("c31-invariance");
        var first = await Build("consumer", "provider", key);
        var renamed = await Build("sink", "source", key);

        // Name-bijection equivariance: rename every fiber consistently and the
        // observation structure (states, declared sides, bindings) is unchanged.
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["consumer"] = "sink",
            ["provider"] = "source",
        };
        static string NormalizeBinding(string binding, IReadOnlyDictionary<Guid, string> names)
        {
            var separator = binding.IndexOf("->", StringComparison.Ordinal);
            if (separator < 0)
            {
                return binding;
            }

            var idText = binding[(separator + 2)..];
            return Guid.TryParse(idText, out var id) && names.TryGetValue(id, out var name)
                ? $"{binding[..(separator + 2)]}{name}"
                : binding;
        }

        static string Canonical(RuntimeSnapshot snapshot, Func<string, string>? rename = null)
        {
            var names = snapshot.Fibers.ToDictionary(
                fiber => fiber.Id,
                fiber => rename?.Invoke(fiber.Name) ?? fiber.Name);
            return string.Join("|", snapshot.Fibers
                .OrderBy(fiber => names[fiber.Id], StringComparer.Ordinal)
                .Select(fiber => $"{names[fiber.Id]}:{fiber.State}:{fiber.Generation}:" +
                                 $"{string.Join(",", fiber.Requires.OrderBy(name => name, StringComparer.Ordinal))}:" +
                                 $"{string.Join(",", fiber.Provides.OrderBy(name => name, StringComparer.Ordinal))}:" +
                                 $"{string.Join(",", fiber.BoundProviders.Select(binding => NormalizeBinding(binding, names)).OrderBy(binding => binding, StringComparer.Ordinal))}"));
        }

        Assert.Equal(Canonical(first, name => map[name]), Canonical(renamed));
        Assert.True(ObservationEquivalence.Equivalent(first, first));

        // Vestigial records from the old episode are invisible: after retire and
        // reinsert, only the new provider id appears in any binding or fiber row.
        await using var runtime = new Runtime();
        var oldProvider = await runtime.LoadAsync(new Step(
            "provider",
            context =>
            {
                context.Provide(key, 1);
                return Task.CompletedTask;
            },
            provide: [key]));
        var consumer = await runtime.LoadAsync(new Step(
            "consumer",
            context =>
            {
                context.Require(key);
                return Task.CompletedTask;
            },
            inject: [key]));
        var oldId = oldProvider.Id;
        await runtime.DisposeAsync(oldProvider);
        var newProvider = await runtime.LoadAsync(new Step(
            "provider",
            context =>
            {
                context.Provide(key, 2);
                return Task.CompletedTask;
            },
            provide: [key]));

        var snapshot = runtime.Snapshot();
        Assert.DoesNotContain(snapshot.Fibers, fiber => fiber.Id == oldId);
        Assert.DoesNotContain(snapshot.Fibers.SelectMany(fiber => fiber.BoundProviders), binding => binding.Contains(oldId.ToString(), StringComparison.Ordinal));
        Assert.Contains($"{key.Name}->{newProvider.Id}", SnapshotOf(runtime, "consumer").BoundProviders.Single());
        Assert.Equal(2, snapshot.Fibers.Count);
    }

    [Fact]
    public async Task C33_FailureIsIsolatedOnTheFiberAndBlocksReentry()
    {
        var source = new InvalidOperationException("raise");
        var childApplies = 0;
        var undone = 0;

        await using var runtime = new Runtime();
        var parent = await runtime.LoadAsync(new Step(
            "parent",
            async context =>
            {
                await context.LoadChildAsync(new Step(
                    "failing-child",
                    async childContext =>
                    {
                        childApplies++;
                        await childContext.EffectAsync(
                            () => ValueTask.CompletedTask,
                            () =>
                            {
                                undone++;
                                return ValueTask.CompletedTask;
                            });
                        throw source;
                    }));
                return null;
            }));

        var child = Assert.Single(parent.Children);
        Assert.Equal(FiberState.Failed, child.State);
        Assert.Same(source, child.Failure);
        Assert.Equal(1, undone); // inverse covers only the failing iteration
        Assert.Equal(FiberState.Active, parent.State); // no propagation to owner
        Assert.Null(parent.Failure);

        await runtime.LoadAsync(new Step("unrelated", _ => Task.CompletedTask));
        Assert.Equal(1, childApplies); // no automatic retry / reentry

        // Reclaim the failed fiber explicitly; runtime close must still be clean.
        await runtime.DisposeAsync(child);
        Assert.Equal(FiberState.Gone, child.State);
        Assert.Equal(FiberState.Active, parent.State);
        Assert.Equal(1, childApplies);
    }

    [Fact]
    public async Task C35_ReversibleBoundaryCoversOwnedEffectsButNotExternalEmission()
    {
        var owned = new List<string>();
        var external = new List<string>();
        Action<string> emit = payload => external.Add(payload); // outside Context

        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new Step(
            "boundary",
            async context =>
            {
                await context.EffectAsync(
                    () =>
                    {
                        owned.Add("owned");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        owned.Remove("owned");
                        return ValueTask.CompletedTask;
                    });
                emit("external");
                return null;
            }));

        Assert.Equal(new[] { "owned" }, owned);
        Assert.Equal(new[] { "external" }, external);

        await runtime.DisposeAsync(fiber);

        Assert.Empty(owned);
        Assert.Equal(new[] { "external" }, external); // no restoration promised
        Assert.Equal(FiberState.Gone, fiber.State);
    }

    [Fact]
    public async Task C36_DependencyCyclesAreRejectedAtMountWithAnExplicitChain()
    {
        var aKey = new Key<int>("c36-a");
        var bKey = new Key<int>("c36-b");
        await using var runtime = new Runtime();

        var a = await runtime.LoadAsync(new Step(
            "A",
            context =>
            {
                context.Provide(aKey, 1);
                return Task.CompletedTask;
            },
            inject: [bKey],
            provide: [aKey]));
        Assert.Equal(FiberState.Pending, a.State);

        var cycle = await Assert.ThrowsAsync<DependencyCycleException>(() => runtime.LoadAsync(new Step(
            "B",
            context =>
            {
                context.Provide(bKey, 2);
                return Task.CompletedTask;
            },
            inject: [aKey],
            provide: [bKey])));
        Assert.Contains("A", cycle.Message, StringComparison.Ordinal);
        Assert.Contains("B", cycle.Message, StringComparison.Ordinal);
        Assert.Contains("->", cycle.Message, StringComparison.Ordinal);
        Assert.Single(runtime.Snapshot().Fibers); // the rejected entry never becomes a fiber
        Assert.Equal(FiberState.Pending, runtime.Snapshot().Fibers.Single().State);

        var selfKey = new Key<int>("c36-self");
        var selfCycle = await Assert.ThrowsAsync<DependencyCycleException>(() => runtime.LoadAsync(new Step(
            "Self",
            context =>
            {
                context.Provide(selfKey, 1);
                return Task.CompletedTask;
            },
            inject: [selfKey],
            provide: [selfKey])));
        Assert.Contains("Self", selfCycle.Message, StringComparison.Ordinal);
        Assert.Single(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task C37_DeclaredCapabilitiesAreArbitratedAndMetaCanRefinePolicy()
    {
        var secret = new Key<int>("c37-secret");
        var unowned = new Key<int>("c37-unowned");
        var policy = new MetaKey<int, int>("c37-policy", 0, (declared, overridden) => overridden == 0 ? declared : overridden);
        Exception? undeclaredGet = null;
        Exception? undeclaredSet = null;
        var policyReads = new List<int>();

        await using var runtime = new Runtime();
        await runtime.LoadAsync(new Step(
            "secret-provider",
            context =>
            {
                context.Provide(secret, 42);
                return Task.CompletedTask;
            },
            provide: [secret]));

        var intruder = await runtime.LoadAsync(new Step(
            "intruder",
            context =>
            {
                undeclaredGet = Capture(() => context.Require(secret));
                undeclaredSet = Capture(() => context.Provide(unowned, 1));
                return Task.CompletedTask;
            }));
        Assert.IsType<UndeclaredCapabilityException>(undeclaredGet);
        Assert.IsType<UndeclaredCapabilityException>(undeclaredSet);

        var owner = await runtime.LoadAsync(new Step(
            "policy-owner",
            async context =>
            {
                Assert.Equal(42, context.Require(secret));
                context.DeclareMeta(policy, 10);
                policyReads.Add(context.Meta(policy));
                context.OverrideMeta(policy, 99);
                var child = await context.LoadChildAsync(new Step(
                    "policy-reader",
                    readerContext =>
                    {
                        policyReads.Add(readerContext.Meta(policy));
                        return Task.CompletedTask;
                    },
                    inject: [policy]));
                return null;
            },
            inject: [secret, policy]));
        var childFiber = Assert.Single(owner.Children);
        Assert.Equal(owner.Id, childFiber.Parent!.Id);
        Assert.Equal(new[] { 10, 99 }, policyReads);
        Assert.Equal(1, owner.Generation); // policy change did not reload
        Assert.Equal(FiberState.Active, owner.State);
    }

    [Fact]
    public async Task C39_BackendNeutralFactoriesComposeThroughTheCoreContract()
    {
        // The real IPC/WASM backends live in other assemblies. What this
        // conformance case asserts is the backend-neutral contract they all
        // implement: same typed keys, same effect reversal, same observation,
        // and runtime introduction/withdrawal through a declaration registry.
        var hostKey = new Key<int>("c39-host");
        var undoLog = new List<string>();
        var kinds = new[] { "managed", "wasm", "proc" };

        await using var runtime = new Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.ExternalCapabilities.Add(hostKey);

        foreach (var kind in kinds)
        {
            var output = new Key<int>($"c39-{kind}");
            registry.RegisterFactory(kind, _ => new Step(
                $"{kind}-worker",
                async context =>
                {
                    context.Require(hostKey);
                    context.Provide(output, 1);
                    await context.EffectAsync(
                        () => ValueTask.CompletedTask,
                        () =>
                        {
                            undoLog.Add(kind);
                            return ValueTask.CompletedTask;
                        });
                    return null;
                },
                inject: [hostKey],
                provide: [output]));
        }

        await runtime.LoadAsync(new Step(
            "host",
            context =>
            {
                context.Provide(hostKey, 5);
                return Task.CompletedTask;
            },
            provide: [hostKey]));

        static PluginManifest Declaration(string id, string type) =>
            new(id, type, true, new Dictionary<string, object?>(), [], []);

        async Task<IReadOnlyDictionary<string, Guid>> ApplyAndObserve()
        {
            await registry.ApplyAsync(kinds.Select(kind => Declaration(kind, kind)).ToArray());
            Assert.Equal(3, registry.Entries.Count);
            foreach (var kind in kinds)
            {
                Assert.Equal(FiberState.Active, registry.Entries[kind].Fiber!.State);
            }

            Assert.Equal(4, runtime.Snapshot().Fibers.Count);
            return registry.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Fiber!.Id);
        }

        var firstIds = await ApplyAndObserve();
        var firstFibers = registry.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Fiber!);
        await registry.ApplyAsync(Array.Empty<PluginManifest>());
        Assert.All(firstFibers.Values, fiber => Assert.Equal(FiberState.Gone, fiber.State));

        var secondIds = await ApplyAndObserve();
        Assert.DoesNotContain(firstIds["wasm"], secondIds.Values);

        await registry.ApplyAsync(Array.Empty<PluginManifest>()); // withdrawal in declaration order
        Assert.Single(runtime.Snapshot().Fibers); // only the host remains
        Assert.Equal(
            kinds.Concat(kinds).OrderBy(kind => kind, StringComparer.Ordinal),
            undoLog.OrderBy(kind => kind, StringComparer.Ordinal));
    }

    private static FiberSnapshot SnapshotOf(Runtime runtime, string name) =>
        runtime.Snapshot().Fibers.Single(fiber => fiber.Name == name);

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private sealed class Step : Component
    {
        private readonly Func<Context, ValueTask<Cleanup?>> _apply;

        public Step(
            string name,
            Func<Context, Task> apply,
            IReadOnlyList<ICapabilityKey>? inject = null,
            IReadOnlyList<ICapabilityKey>? provide = null)
            : this(
                name,
                async context =>
                {
                    await apply(context);
                    return null;
                },
                inject,
                provide)
        {
        }

        public Step(
            string name,
            Func<Context, ValueTask<Cleanup?>> apply,
            IReadOnlyList<ICapabilityKey>? inject = null,
            IReadOnlyList<ICapabilityKey>? provide = null)
        {
            Name = name;
            _apply = apply;
            InjectKeys = inject ?? [];
            ProvideKeys = provide ?? [];
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => InjectKeys;

        public override IReadOnlyList<ICapabilityKey> Provide() => ProvideKeys;

        public override ValueTask<Cleanup?> ApplyAsync(Context context) => _apply(context);

        private IReadOnlyList<ICapabilityKey> InjectKeys { get; }

        private IReadOnlyList<ICapabilityKey> ProvideKeys { get; }
    }

    private sealed class Steps : Component, IIterativeComponent
    {
        private readonly Func<Context, IAsyncEnumerable<EffectRegistration>> _iterate;

        public Steps(
            string name,
            Func<Context, IAsyncEnumerable<EffectRegistration>> iterate,
            IReadOnlyList<ICapabilityKey>? inject = null)
        {
            Name = name;
            _iterate = iterate;
            InjectKeys = inject ?? [];
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => InjectKeys;

        public override ValueTask<Cleanup?> ApplyAsync(Context context) => throw new NotSupportedException();

        public IAsyncEnumerable<EffectRegistration> IterateAsync(Context context) => _iterate(context);

        private IReadOnlyList<ICapabilityKey> InjectKeys { get; }
    }
}
