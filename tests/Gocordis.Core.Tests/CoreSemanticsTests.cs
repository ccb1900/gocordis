using Gocordis.Core;
using Xunit;

namespace Gocordis.Core.Tests;

public sealed class CoreSemanticsTests
{
    [Fact]
    public async Task DeclaredRequireAndProvideActivateBothFibers()
    {
        var answer = new Key<int>("answer");
        var observed = 0;

        await using var runtime = new Runtime();
        var provider = new DelegateComponent(
            "provider",
            async context =>
            {
                context.Provide(answer, 42);
                return null;
            },
            provide: [answer]);
        var consumer = new DelegateComponent(
            "consumer",
            async context =>
            {
                observed = context.Require(answer);
                return null;
            },
            inject: [answer]);

        var providerFiber = await runtime.LoadAsync(provider);
        var consumerFiber = await runtime.LoadAsync(consumer);

        Assert.Equal(FiberState.Active, providerFiber.State);
        Assert.Equal(FiberState.Active, consumerFiber.State);
        Assert.Equal(42, observed);
    }

    [Fact]
    public async Task CoreEmitDispatchesInRegistrationOrderAndThrowsTheFirstHandlerFailure()
    {
        var key = new EventKey<string>("order");
        var seen = new List<string>();
        Exception? observedFailure = null;

        await using var runtime = new Runtime();
        var channel = new DelegateComponent(
            "channel",
            async context =>
            {
                context.Provide(key, "channel");
                return null;
            },
            provide: [key]);
        await runtime.LoadAsync(channel);

        var component = new DelegateComponent(
            "emitter",
            async context =>
            {
                await context.OnAsync(key, value =>
                {
                    seen.Add("first:" + value);
                    return ValueTask.CompletedTask;
                });
                await context.OnAsync(key, _ => throw new InvalidOperationException("boom"));
                await context.OnAsync(key, value =>
                {
                    seen.Add("third:" + value);
                    return ValueTask.CompletedTask;
                });
                try
                {
                    await context.EmitAsync(key, "x");
                }
                catch (Exception ex)
                {
                    observedFailure = ex;
                }

                return null;
            },
            inject: [key]);

        var fiber = await runtime.LoadAsync(component);

        Assert.True(fiber.State == FiberState.Active, $"state={fiber.State} failure={fiber.Failure}");
        Assert.Equal(new[] { "first:x", "third:x" }, seen);
        Assert.Equal("boom", observedFailure?.Message);
    }

    [Fact]
    public async Task UndeclaredRequireFailsTheFiber()
    {
        var key = new Key<int>("hidden");

        await using var runtime = new Runtime();
        var consumer = new DelegateComponent("bad-consumer", context =>
        {
            context.Require(key);
            return ValueTask.FromResult<Cleanup?>(null);
        });

        var fiber = await runtime.LoadAsync(consumer);

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.IsType<UndeclaredCapabilityException>(fiber.Failure);
    }

    [Fact]
    public async Task ContextUsesCapturedDeclarationsWhenTheComponentMutatesAfterLoad()
    {
        await using var runtime = new Runtime();
        var dependency = new Key<int>("captured-dependency");
        var ownKey = new Key<int>("captured-own");
        var lateKey = new Key<int>("not-declared-at-load");
        var seen = 0;
        var lateRejected = false;
        await runtime.LoadAsync(new DelegateComponent("upstream", context =>
        {
            context.Provide(dependency, 41);
            return ValueTask.FromResult<Cleanup?>(null);
        }, provide: [dependency]));
        var component = new MutatingDeclarationComponent(
            "changing-declarations",
            async context =>
            {
                context.Provide(ownKey, 1);
                seen = context.Require(dependency);
                return () =>
                {
                    // Teardown still reads the original captured declarations:
                    // Require during Cleanup stays legal, a later key does not.
                    seen += context.Require(dependency);
                    try
                    {
                        context.Require(lateKey);
                    }
                    catch (UndeclaredCapabilityException)
                    {
                        lateRejected = true;
                    }

                    return ValueTask.CompletedTask;
                };
            },
            [dependency],
            [ownKey]);

        var fiber = await runtime.LoadAsync(component);
        Assert.Equal(FiberState.Active, fiber.State);
        component.ClearDeclarations();
        Assert.Empty(component.Inject());
        Assert.Empty(component.Provide());

        await runtime.DisposeAsync(fiber);

        Assert.Equal(82, seen);
        Assert.True(lateRejected);
        Assert.Equal(FiberState.Gone, fiber.State);
    }

    [Fact]
    public async Task UndeclaredProvideFailsTheFiber()
    {
        var key = new Key<int>("hidden");

        await using var runtime = new Runtime();
        var provider = new DelegateComponent("bad-provider", context =>
        {
            context.Provide(key, 1);
            return ValueTask.FromResult<Cleanup?>(null);
        });

        var fiber = await runtime.LoadAsync(provider);

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.IsType<UndeclaredCapabilityException>(fiber.Failure);
    }

    [Fact]
    public async Task DuplicateProviderIsRejectedAtLoad()
    {
        var key = new Key<int>("single");
        await using var runtime = new Runtime();
        var provider = new DelegateComponent(
            "provider",
            async context =>
            {
                context.Provide(key, 1);
                return null;
            },
            provide: [key]);

        await runtime.LoadAsync(provider);
        await Assert.ThrowsAsync<DuplicateProviderException>(() => runtime.LoadAsync(provider));
    }

    [Fact]
    public async Task DependencyLossMovesConsumerToPendingAndRestoresIt()
    {
        var key = new Key<string>("service");
        var observed = new List<string>();

        await using var runtime = new Runtime();
        var firstProvider = Provider("p1", "v1");
        var consumer = new DelegateComponent(
            "consumer",
            async context =>
            {
                observed.Add(context.Require(key));
                return null;
            },
            inject: [key]);

        var p1 = await runtime.LoadAsync(firstProvider);
        var c = await runtime.LoadAsync(consumer);
        Assert.Equal(FiberState.Active, c.State);

        await runtime.DisposeAsync(p1);
        Assert.Equal(FiberState.Pending, c.State);

        await runtime.LoadAsync(Provider("p2", "v2"));
        Assert.Equal(FiberState.Active, c.State);
        Assert.Equal(new[] { "v1", "v2" }, observed);

        DelegateComponent Provider(string name, string value) => new(
            name,
            async context =>
            {
                context.Provide(key, value);
                return null;
            },
            provide: [key]);
    }

    [Fact]
    public async Task EffectsUnwindInStrictLifoOrder()
    {
        var log = new List<string>();
        await using var runtime = new Runtime();
        var component = new DelegateComponent("effects", async context =>
        {
            for (var i = 1; i <= 3; i++)
            {
                var current = i;
                await ctxEffect(context, current);
            }

            return null;
        });

        var fiber = await runtime.LoadAsync(component);
        await runtime.DisposeAsync(fiber);

        Assert.Equal(new[] { "undo:3", "undo:2", "undo:1" }, log.Where(entry => entry.StartsWith("undo")).ToArray());

        async ValueTask ctxEffect(Context context, int current)
        {
            await context.EffectAsync(
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
    }

    [Fact]
    public async Task ApplyFailureUnwindsInstalledEffectsAndNeverRetries()
    {
        var undone = false;
        await using var runtime = new Runtime();
        var component = new DelegateComponent("boom", async context =>
        {
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () =>
                {
                    undone = true;
                    return ValueTask.CompletedTask;
                });
            throw new InvalidOperationException("boom");
        });

        var fiber = await runtime.LoadAsync(component);

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.IsType<InvalidOperationException>(fiber.Failure);
        Assert.True(undone);
        Assert.Equal(1, fiber.Generation);
    }

    [Fact]
    public async Task ConcurrentInvokeAndDisposeNeverUseAReleasedEndpoint()
    {
        for (var round = 0; round < 8; round++)
        {
            var entered = 0;
            var released = 0;
            var afterReleaseCalls = 0;
            var invokeTasks = new List<Task>();
            var endpoint = new DelegatingCapabilityEndpoint(
                new CapabilityId("stress", "svc"),
                new CapabilityContract(
                    new CapabilityId("stress", "svc"), CapabilityValueKind.Endpoint, null),
                async (_, _) =>
                {
                    Interlocked.Increment(ref entered);
                    await Task.Delay(1);
                    if (Volatile.Read(ref released) != 0)
                    {
                        Interlocked.Increment(ref afterReleaseCalls);
                    }

                    return CapabilityEnvelope.Text("ok");
                },
                () =>
                {
                    Interlocked.Exchange(ref released, 1);
                    return ValueTask.CompletedTask;
                });

            for (var i = 0; i < 32; i++)
            {
                invokeTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await endpoint.InvokeAsync(
                            CapabilityEnvelope.Text("x"), CancellationToken.None);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Calls arriving after the linearization point are
                        // rejected; they never touch the released resource.
                    }
                }));
            }

            var disposals = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(async () => await endpoint.DisposeAsync()))
                .ToArray();
            await Task.WhenAll(invokeTasks.Concat(disposals));

            Assert.Equal(0, afterReleaseCalls);
            Assert.True(Volatile.Read(ref released) == 1);
            Assert.Equal(0, entered - entered); // silence analyzer on unused capture
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await endpoint.InvokeAsync(CapabilityEnvelope.Text("x"), CancellationToken.None));
        }
    }

    [Fact]
    public async Task ReverseConsumerIndexTracksWithdrawalAndRebinding()
    {
        var key = new Key<int>("indexed");
        await using var runtime = new Runtime();
        var provider = new DelegateComponent("provider", async context =>
        {
            context.Provide(key, 7);
            return null;
        }, provide: [key]);
        var providerFiber = await runtime.LoadAsync(provider);

        var consumers = new List<Fiber>();
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            consumers.Add(await runtime.LoadAsync(new DelegateComponent(
                $"consumer-{index}",
                async context =>
                {
                    _ = context.Require(key);
                    return null;
                },
                inject: [key])));
        }

        Assert.All(consumers, fiber => Assert.Equal(FiberState.Active, fiber.State));

        // One consumer leaves; the index must drop it without touching others.
        await runtime.DisposeAsync(consumers[0]);
        await runtime.DisposeAsync(providerFiber);
        // The decision loop settles the remaining consumers asynchronously:
        // wait for the withdrawal transition instead of sampling one snapshot.
        foreach (var consumer in consumers.Skip(1))
        {
            await WaitForStateAsync(runtime, consumer.Id, FiberState.Pending);
        }

        Assert.Null(consumers[1].Failure);

        // A fresh provider re-activates only the remaining indexed consumers.
        var replacement = await runtime.LoadAsync(new DelegateComponent("replacement", async context =>
        {
            context.Provide(key, 9);
            return null;
        }, provide: [key]));
        await WaitForStateAsync(runtime, replacement.Id, FiberState.Active);
        foreach (var consumer in consumers.Skip(1))
        {
            await WaitForStateAsync(runtime, consumer.Id, FiberState.Active);
        }
    }

    [Fact]
    public async Task CommandQueueObservationDrainsAfterQuiescence()
    {
        await using var runtime = new Runtime();
        var key = new Key<int>("queue-depth");
        await runtime.LoadAsync(new DelegateComponent("provider", async context =>
        {
            context.Provide(key, 7);
            return null;
        }, provide: [key]));
        Assert.True(runtime.PendingCommands >= 0);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (runtime.PendingCommands != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, runtime.PendingCommands);
    }

    [Fact]
    public async Task IterativeStepFailureIsAttributedToItsRegistrationLabel()
    {
        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new IterativeFailureComponent());

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.Equal("step-2", Assert.Single(runtime.Snapshot().EffectFailures).Label);
        Assert.True(runtime.Snapshot().EffectFailures[0].IsInstall);
        Assert.Equal(1, runtime.Snapshot().EffectFailures[0].SlotIndex);
        Assert.IsType<InvalidOperationException>(fiber.Failure);
    }

    private sealed class IterativeFailureComponent : Component, IIterativeComponent
    {
        public override string Name => "iterative-failure";

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<EffectRegistration> IterateAsync(Context context)
        {
            yield return EffectRegistration.From(
                () => ValueTask.CompletedTask,
                () => ValueTask.CompletedTask,
                "step-1");
            await Task.Yield();
            yield return EffectRegistration.From(
                () => throw new InvalidOperationException("step failed"),
                () => ValueTask.CompletedTask,
                "step-2");
        }
    }

    [Fact]
    public async Task InstallFailureRecordsTheEffectSlotAndLabelWithoutMaskingTheException()
    {
        await using var runtime = new Runtime();
        var component = new DelegateComponent("attributed", async context =>
        {
            await context.EffectAsync(
                () => throw new InvalidOperationException("install blew up"),
                () => ValueTask.CompletedTask,
                "open-resource");
            return null;
        });

        var fiber = await runtime.LoadAsync(component);

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.IsType<InvalidOperationException>(fiber.Failure);
        Assert.Equal("install blew up", fiber.Failure?.Message);

        var record = Assert.Single(runtime.Snapshot().EffectFailures);
        Assert.Equal(fiber.Id, record.FiberId);
        Assert.Equal(0, record.SlotIndex);
        Assert.Equal("open-resource", record.Label);
        Assert.True(record.IsInstall);
        Assert.Equal("InvalidOperationException", record.ExceptionType);
        Assert.Contains("install blew up", record.Message);
        Assert.Same(fiber.Failure, record.Exception);
    }

    [Fact]
    public async Task FailedCompensationInverseIsObservableAndNotReRun()
    {
        var inverseRuns = 0;
        await using var runtime = new Runtime();
        var component = new DelegateComponent("boom", async context =>
        {
            await context.EffectAsync(
                () => throw new InvalidOperationException("install failed"),
                () =>
                {
                    inverseRuns++;
                    throw new InvalidOperationException("inverse blew up");
                });
            return null;
        });

        var fiber = await runtime.LoadAsync(component);

        // Install failure is the fiber surface; the compensation failure is a
        // retained cleanup record, never silently swallowed.
        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.IsType<InvalidOperationException>(fiber.Failure);
        Assert.Equal("install failed", fiber.Failure?.Message);
        Assert.Equal(1, inverseRuns);
        var snapshot = runtime.Snapshot();
        Assert.Contains(snapshot.CleanupFailures, record =>
            record.FiberId == fiber.Id && record.Message == "inverse blew up");
        Assert.Contains(fiber.CleanupFailures, error => error.Message == "inverse blew up");
        Assert.Contains(runtime.Snapshot().EffectFailures, record =>
            !record.IsInstall && record.Message == "inverse blew up");
    }

    [Fact]
    public async Task FailedCompensationInverseThatSucceedsIsMarkedUndone()
    {
        var inverseRuns = 0;
        await using var runtime = new Runtime();
        var component = new DelegateComponent("boom", async context =>
        {
            await context.EffectAsync(
                () => throw new InvalidOperationException("install failed"),
                () =>
                {
                    inverseRuns++;
                    return ValueTask.CompletedTask;
                });
            return null;
        });

        var fiber = await runtime.LoadAsync(component);

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.Equal(1, inverseRuns);
        Assert.Empty(fiber.CleanupFailures);
        Assert.Empty(runtime.Snapshot().CleanupFailures);
    }

    [Fact]
    public async Task EmitSkipsHandlersWhoseOwnerIsWithdrawing()
    {
        var key = new EventKey<string>("withdraw");
        var received = new List<string>();
        await using var runtime = new Runtime();
        var channel = new DelegateComponent("channel", async context =>
        {
            context.Provide(key, "channel");
            return null;
        }, provide: [key]);
        await runtime.LoadAsync(channel);

        var listener = new DelegateComponent("listener", async context =>
        {
            await context.OnAsync(key, value =>
            {
                received.Add(value);
                return ValueTask.CompletedTask;
            });
            return null;
        }, inject: [key]);
        var listenerFiber = await runtime.LoadAsync(listener);

        var emitter = new DelegateComponent("emitter", async context =>
        {
            await runtime.DisposeAsync(listenerFiber);
            await context.EmitAsync(key, "after-withdrawal");
            return null;
        }, inject: [key]);
        await runtime.LoadAsync(emitter);

        Assert.Empty(received);
    }

    [Fact]
    public async Task EmitIsScopedToTheRegistrationRealm()
    {
        var key = new EventKey<string>("scoped");
        var received = new List<string>();
        // Realm identity is reference identity (one namespace = one object).
        var realmX = new Realm("realm-x");
        await using var runtime = new Runtime();
        var channel = new DelegateComponent("channel", async context =>
        {
            context.Provide(key, "channel");
            return null;
        }, provide: [key]);
        await runtime.LoadAsync(channel);

        var listener = new DelegateComponent("listener", async context =>
        {
            await context.OnAsync(key, value =>
            {
                received.Add("realm-listener");
                return ValueTask.CompletedTask;
            });
            return null;
        }, inject: [key]);
        // Per ADR-0001 there is no ancestor fallback: realm-x needs its own
        // channel for the listener to bind and register.
        var realmChannel = new DelegateComponent("realm-channel", async context =>
        {
            context.Provide(key, "channel");
            return null;
        }, provide: [key]);
        await runtime.LoadAsync(realmChannel, realm: realmX);
        await runtime.LoadAsync(listener, realm: realmX);

        // A Default-realm emitter cannot see handlers registered in realm-x.
        var outside = new DelegateComponent("outside", async context =>
        {
            await context.EmitAsync(key, "x");
            return null;
        }, inject: [key]);
        await runtime.LoadAsync(outside);
        Assert.Empty(received);

        // A realm-x emitter sees them.
        var inside = new DelegateComponent("inside", async context =>
        {
            await context.EmitAsync(key, "x");
            return null;
        }, inject: [key]);
        await runtime.LoadAsync(inside, realm: realmX);
        Assert.Equal(["realm-listener"], received);
    }

    [Fact]
    public async Task OverrideMetaUnwindsInStrictLifoOrderToTheDeclaredView()
    {
        var key = new MetaKey<string, string>("route", "base", (declared, overridden) => overridden);
        var observed = new List<string>();
        await using var runtime = new Runtime();
        var component = new DelegateComponent("overrider", async context =>
        {
            context.DeclareMeta(key, "declared");
            observed.Add(context.Meta(key));
            context.OverrideMeta(key, "A");
            observed.Add(context.Meta(key));
            context.OverrideMeta(key, "B");
            observed.Add(context.Meta(key));
            return null;
        },
        inject: [key]);

        var fiber = await runtime.LoadAsync(component);
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal(new[] { "base", "A", "B" }, observed);

        await runtime.DisposeAsync(fiber);
        var snapshot = runtime.Snapshot();
        Assert.DoesNotContain(snapshot.Fibers, candidate => candidate.Name == "overrider");
    }

    [Fact]
    public async Task ParentFailureCascadesToOwnedChildren()
    {
        await using var runtime = new Runtime();
        Fiber? childFiber = null;
        var child = new DelegateComponent("child", _ => ValueTask.FromResult<Cleanup?>(null));
        var parent = new DelegateComponent("parent", async context =>
        {
            childFiber = await context.LoadChildAsync(child);
            throw new InvalidOperationException("parent failed");
        });

        var fiber = await runtime.LoadAsync(parent);
        Assert.NotNull(childFiber);
        Assert.Empty(fiber.Children); // Gone children are detached from their owner.

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.Equal(FiberState.Gone, childFiber.State);
        await childFiber.GoneAsync();
    }

    [Fact]
    public async Task CleanupCanStillRequireItsDependency()
    {
        var key = new Key<int>("resource");
        var valueSeenDuringCleanup = -1;

        await using var runtime = new Runtime();
        var provider = new DelegateComponent(
            "provider",
            async context =>
            {
                context.Provide(key, 7);
                return null;
            },
            provide: [key]);
        var consumer = new DelegateComponent(
            "consumer",
            async context =>
            {
                var captured = context.Require(key);
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        valueSeenDuringCleanup = context.Require(key);
                        return ValueTask.CompletedTask;
                    });
                return null;
            },
            inject: [key]);

        var p = await runtime.LoadAsync(provider);
        var c = await runtime.LoadAsync(consumer);
        await runtime.DisposeAsync(c);

        Assert.Equal(7, valueSeenDuringCleanup);
        Assert.Equal(FiberState.Active, p.State);
    }

    [Fact]
    public async Task EventHandlersAreRemovedWhenOwnerIsDisposed()
    {
        var key = new EventKey<string>("notice");
        var received = new List<string>();

        await using var runtime = new Runtime();
        var listener = new DelegateComponent(
            "listener",
            async context =>
            {
                await context.OnAsync(key, value =>
                {
                    received.Add(value);
                    return ValueTask.CompletedTask;
                });
                return null;
            },
            inject: [key]);
        var emitter = new DelegateComponent(
            "emitter",
            async context =>
            {
                context.Provide(key, "channel");
                return null;
            },
            provide: [key]);
        var trigger = new DelegateComponent(
            "trigger",
            async context =>
            {
                await context.EmitAsync(key, "first");
                return null;
            },
            inject: [key]);

        var listenerFiber = await runtime.LoadAsync(listener);
        var emitterFiber = await runtime.LoadAsync(emitter);
        await runtime.LoadAsync(trigger);
        Assert.Equal(new[] { "first" }, received);

        await runtime.DisposeAsync(listenerFiber);
        await runtime.DisposeAsync(emitterFiber);
        var secondEmitter = new DelegateComponent(
            "emitter2",
            async context =>
            {
                context.Provide(key, "channel");
                return null;
            },
            provide: [key]);
        await runtime.LoadAsync(secondEmitter);
        await runtime.LoadAsync(new DelegateComponent(
            "trigger2",
            async context =>
            {
                await context.EmitAsync(key, "second");
                return null;
            },
            inject: [key]));

        Assert.Equal(new[] { "first" }, received);
    }

    [Fact]
    public async Task DependencyCycleIsRejectedAtMount()
    {
        var aKey = new Key<int>("a");
        var bKey = new Key<int>("b");

        await using var runtime = new Runtime();
        var a = new DelegateComponent(
            "A",
            async context =>
            {
                context.Provide(aKey, 1);
                return null;
            },
            inject: [bKey],
            provide: [aKey]);
        var b = new DelegateComponent(
            "B",
            async context =>
            {
                context.Provide(bKey, 2);
                return null;
            },
            inject: [aKey],
            provide: [bKey]);

        await runtime.LoadAsync(a);
        var exception = await Assert.ThrowsAsync<DependencyCycleException>(() => runtime.LoadAsync(b));
        Assert.Contains("A", exception.Message);
        Assert.Contains("B", exception.Message);
    }

    [Fact]
    public async Task DivertDuringActivationUnwindsWithoutFailure()
    {
        var log = new List<string>();

        await using var runtime = new Runtime();
        var component = new IterativeComponent("blocking", context => BlockingSteps(context, log));
        var loading = runtime.LoadAsync(component);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!log.Contains("install:2") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var fiber = Assert.Single(runtime.Snapshot().Fibers);
        var handle = runtime.GetFiber(fiber.Id);
        Assert.NotNull(handle);

        var disposal = runtime.DisposeAsync(handle);
        var loaded = await loading;
        await disposal;

        Assert.Equal(FiberState.Gone, loaded.State);
        Assert.Null(loaded.Failure);
        Assert.Contains("undo:1", log);
        Assert.DoesNotContain("undo:2", log);

        static async IAsyncEnumerable<EffectRegistration> BlockingSteps(Context context, List<string> log)
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

            await Task.Yield();
            yield return EffectRegistration.From(
                async () =>
                {
                    log.Add("install:2");
                    await Task.Delay(Timeout.Infinite, context.CancellationToken);
                },
                () =>
                {
                    log.Add("undo:2");
                    return ValueTask.CompletedTask;
                });
        }
    }


    [Fact]
    public async Task DeclaredEventKeyCanBeRequiredAsACapability()
    {
        var channel = new EventKey<string>("channel");
        var observed = string.Empty;

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(new DelegateComponent(
            "provider",
            async context =>
            {
                context.Provide(channel, "open");
                return null;
            },
            provide: [channel]));

        var consumer = await runtime.LoadAsync(new DelegateComponent(
            "consumer",
            async context =>
            {
                observed = context.Require(channel);
                return null;
            },
            inject: [channel]));

        Assert.Equal(FiberState.Active, consumer.State);
        Assert.Equal("open", observed);
    }


    [Fact]
    public async Task FailedEffectInstallationCompensatesPartialSideEffects()
    {
        var compensated = 0;
        await using var runtime = new Gocordis.Core.Runtime();
        var component = new DelegateComponent("failing-effect", async context =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.EffectAsync(
                async () =>
                {
                    await ValueTask.CompletedTask;
                    throw new InvalidOperationException("install failed after side effect");
                },
                () =>
                {
                    compensated++;
                    return ValueTask.CompletedTask;
                }).AsTask());
            return null;
        });

        var fiber = await runtime.LoadAsync(component);

        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal(1, compensated);
    }

    [Fact]
    public async Task StaleActivationContextCannotMutateTheFiber()
    {
        var key = new Key<int>("volatile-dependency");
        Context? captured = null;

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = new DelegateComponent(
            "provider",
            async context =>
            {
                context.Provide(key, 1);
                return null;
            },
            provide: [key]);
        var consumer = new DelegateComponent(
            "consumer",
            async context =>
            {
                captured ??= context;
                context.Require(key);
                return null;
            },
            inject: [key]);

        var first = await runtime.LoadAsync(provider);
        await runtime.LoadAsync(consumer);
        await runtime.DisposeAsync(first);
        await runtime.LoadAsync(new DelegateComponent(
            "provider2",
            async context =>
            {
                context.Provide(key, 2);
                return null;
            },
            provide: [key]));

        Assert.NotNull(captured);
        Assert.Throws<DivertedException>(() => captured!.Provide(new Key<int>("local"), 1));
    }

    [Fact]
    public async Task RealmsIsolateTheSameKeyIntoIndependentProviders()
    {
        var key = new Key<string>("scoped");
        var left = new Realm("left");
        var right = new Realm("right");
        var seen = new List<string>();

        await using var runtime = new Runtime();
        await runtime.LoadAsync(new DelegateComponent(
            "left-provider",
            async context =>
            {
                context.Provide(key, "L");
                return null;
            },
            provide: [key]), realm: left);
        await runtime.LoadAsync(new DelegateComponent(
            "right-provider",
            async context =>
            {
                context.Provide(key, "R");
                return null;
            },
            provide: [key]), realm: right);

        foreach (var realm in new[] { left, right, left })
        {
            await runtime.LoadAsync(new DelegateComponent(
                $"consumer-{realm.Name}",
                async context =>
                {
                    seen.Add(context.Require(key));
                    return null;
                },
                inject: [key]), realm: realm);
        }

        Assert.Equal(new[] { "L", "R", "L" }, seen);
    }

    [Fact]
    public async Task IsolateReassignsAKeyAndIsReversible()
    {
        var key = new Key<string>("routed");
        var special = new Realm("special");
        var seen = new List<string>();

        await using var runtime = new Runtime();
        await runtime.LoadAsync(new DelegateComponent(
            "default-provider",
            async context =>
            {
                context.Provide(key, "default");
                return null;
            },
            provide: [key]));
        await runtime.LoadAsync(new DelegateComponent(
            "special-provider",
            async context =>
            {
                context.Provide(key, "special");
                return null;
            },
            provide: [key]), realm: special);

        var consumer = new DelegateComponent(
            "consumer",
            async context =>
            {
                seen.Add(context.Require(key));
                context.Isolate(key, special);
                context.ThrowIfDiverted();
                return null;
            },
            inject: [key]);
        var fiber = await runtime.LoadAsync(consumer);

        Assert.Equal("default", Assert.Single(seen));

        await runtime.DisposeAsync(fiber);
        await runtime.LoadAsync(new DelegateComponent(
            "consumer-again",
            async context =>
            {
                seen.Add(context.Require(key));
                return null;
            },
            inject: [key]));

        Assert.Equal(new[] { "default", "default" }, seen);
    }

    [Fact]
    public async Task MetaInterceptionIsRightBiasedAndDoesNotReload()
    {
        var tag = new MetaKey<int, string>(
            "tag",
            string.Empty,
            (declared, overridden) => overridden.Length == 0 ? declared : overridden);
        var observed = new List<string>();

        await using var runtime = new Runtime();
        var component = new DelegateComponent(
            "meta-consumer",
            async context =>
            {
                context.DeclareMeta(tag, "declared");
                observed.Add(context.Meta(tag));
                context.OverrideMeta(tag, "overridden");
                observed.Add(context.Meta(tag));
                return null;
            },
            inject: [tag]);

        var fiber = await runtime.LoadAsync(component);

        Assert.Equal(new[] { "declared", "overridden" }, observed);
        Assert.Equal(FiberState.Active, fiber.State);
    }

    [Fact]
    public async Task ConcurrentLoadAndDisposeStayLinearizable()
    {
        await using var runtime = new Runtime();
        var keys = Enumerable.Range(0, 20).Select(i => new Key<int>($"key-{i}")).ToArray();

        var fibers = await Task.WhenAll(keys.Select(key =>
        {
            var component = new DelegateComponent(
                $"provider-{key.Name}",
                async context =>
                {
                    context.Provide(key, 1);
                    return null;
                },
                provide: [key]);
            return runtime.LoadAsync(component);
        }));

        Assert.All(fibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
        Assert.Equal(20, runtime.Snapshot().Fibers.Count);

        await Task.WhenAll(fibers.Select(fiber => runtime.DisposeAsync(fiber)));
        Assert.All(fibers, fiber => Assert.Equal(FiberState.Gone, fiber.State));
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task UndeclaredMetaReadAndOverrideFailWhileDeclaredAccessKeepsWorking()
    {
        var permitted = new MetaKey<int, string>(
            "declared-meta", string.Empty, (_, incoming) => incoming);
        var forbidden = new MetaKey<int, string>(
            "undeclared-meta", string.Empty, (_, incoming) => incoming);
        Exception? readFailure = null;
        Exception? overrideFailure = null;
        string? observed = null;

        await using var runtime = new Runtime();
        var component = new DelegateComponent(
            "meta-declaration-probe",
            async context =>
            {
                readFailure = Capture(() => context.Meta(forbidden));
                overrideFailure = Capture(() => context.OverrideMeta(forbidden, "x"));
                context.DeclareMeta(permitted, "declared");
                context.OverrideMeta(permitted, "overridden");
                observed = context.Meta(permitted);
                return null;
            },
            inject: [permitted]);

        var fiber = await runtime.LoadAsync(component);
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.IsType<UndeclaredCapabilityException>(readFailure);
        Assert.IsType<UndeclaredCapabilityException>(overrideFailure);
        Assert.Equal("overridden", observed);
    }

    [Fact]
    public async Task UndeclaredEmitFailsWhileDeclaredInjectStillDispatches()
    {
        var channel = new EventKey<string>("declared-event");
        var provided = new EventKey<string>("provided-event");
        var forbidden = new EventKey<string>("undeclared-event");
        var provideOnly = new EventKey<string>("provide-only-event");
        var received = new List<string>();
        Exception? undeclaredFailure = null;
        Exception? provideOnlyFailure = null;
        var providedDelivery = string.Empty;

        await using var runtime = new Runtime();
        await runtime.LoadAsync(new DelegateComponent(
            "channel",
            async context =>
            {
                context.Provide(channel, "channel");
                return null;
            },
            provide: [channel]));
        await runtime.LoadAsync(new DelegateComponent(
            "event-provider",
            async context =>
            {
                context.Provide(provided, "provider");
                return null;
            },
            provide: [provided]));

        await runtime.LoadAsync(new DelegateComponent(
            "listener",
            async context =>
            {
                await context.OnAsync(channel, value => { received.Add(value); return ValueTask.CompletedTask; });
                await context.OnAsync(provided, value =>
                {
                    providedDelivery = value;
                    return ValueTask.CompletedTask;
                });
                return null;
            },
            inject: [channel, provided]));

        var emitter = await runtime.LoadAsync(new DelegateComponent(
            "emitter",
            async context =>
            {
                await context.EmitAsync(channel, "declared");
                undeclaredFailure = await CaptureAsync(() => context.EmitAsync(forbidden, "forbidden").AsTask());
                return null;
            },
            inject: [channel]));

        var trigger = await runtime.LoadAsync(new DelegateComponent(
            "provided-trigger",
            async context =>
            {
                await context.EmitAsync(provided, "provided");
                return null;
            },
            inject: [provided]));

        var provideOnlyEmitter = await runtime.LoadAsync(new DelegateComponent(
            "provide-only-emitter",
            async context =>
            {
                context.Provide(provideOnly, "channel");
                provideOnlyFailure = await CaptureAsync(
                    () => context.EmitAsync(provideOnly, "rejected").AsTask());
                return null;
            },
            provide: [provideOnly]));

        Assert.Equal(FiberState.Active, emitter.State);
        Assert.Equal(FiberState.Active, trigger.State);
        Assert.Equal(FiberState.Active, provideOnlyEmitter.State);
        Assert.IsType<UndeclaredCapabilityException>(undeclaredFailure);
        // A provider may emit on its own declared channel: Provide is authority,
        // and requiring Inject would create a false self-dependency.
        Assert.Null(provideOnlyFailure);
        Assert.Equal(new[] { "declared" }, received);
        Assert.Equal("provided", providedDelivery);
    }

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

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static async Task WaitForStateAsync(Runtime runtime, Guid fiberId, FiberState expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var observed = runtime.Snapshot().Fibers.FirstOrDefault(fiber => fiber.Id == fiberId);
            if (observed?.State == expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"fiber {fiberId} did not reach {expected} within 5s");
    }
}
