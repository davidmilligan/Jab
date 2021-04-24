using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using Xunit;

namespace Jab.Test
{
    public class JabTests
    {
        [Fact]
        public void ConstructorFieldArgumentsInitialized()
        {
            var (b, c) = (new B(), new C());
            Assert.True(new A(b, c).CheckB(b));
        }

        [Fact]
        public void ConstructorPropertyArgumentsInitialized()
        {
            var (b, c) = (new B(), new C());
            Assert.Equal(c, new A(b, c).C);
        }

        [Fact]
        public void InheritedArgumentsInitialized()
        {
            var (b, c, f, g) = (new B(), new C(), new F(), new G());
            Assert.Equal(c, new E(g, f, b, c).C);
            Assert.Equal(f, new E(g, f, b, c).F);
            Assert.Equal(g, new E(g, f, b, c).G);
        }

        [Fact]
        public void ServicesAreRegistered()
        {
            var services = new ServiceCollection();
            services.Jab();
            var provider = services.BuildServiceProvider();
            var a = provider.GetService<A>();
            Assert.NotNull(a);
            Assert.NotNull(a?.C);
        }

        [Fact]
        public void ServiceRegistrationHaveCorrectLifetime()
        {
            var services = new ServiceCollection();
            services.Jab();
            var provider = services.BuildServiceProvider();
            var a1 = provider.CreateScope().ServiceProvider.GetRequiredService<A>();
            var a2 = provider.CreateScope().ServiceProvider.GetRequiredService<A>();
            Assert.NotEqual(a1, a2);
            Assert.Equal(a1.C, a2.C);
        }

        [Fact]
        public void ILoggerUsesEnclosingClassAsGenericTypeArgument()
        {
            var services = new ServiceCollection();
            var mockProvider = new Mock<ILoggerProvider>();
            mockProvider.Setup(t => t.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
            services.AddLogging(t => t.AddProvider(mockProvider.Object));
            services.Jab();
            var logging = services.BuildServiceProvider().GetRequiredService<Logging>();
            logging.Logger.LogInformation("Test");
            mockProvider.Verify(t => t.CreateLogger(typeof(Logging).FullName));
        }
    }

    [Transient]
    public partial class A
    {
        [Jab] private B b;
        [Jab] public C C { get; }

        public bool CheckB(B expected) => b == expected;
    }

    [Transient] public class B { }

    [Singleton] public class C { }

    public partial class D : A
    {
        [Jab] public F F { get; }
    }

    public partial class E : D
    {
        [Jab] public G G { get; }
    }

    [Singleton] public class F { }

    [Singleton] public class G { }

    [Jab] public partial class H : D { }

    [Transient]
    public partial class Logging
    {
        [Jab] public ILogger Logger { get; }
    }
}
