using Microsoft.Extensions.DependencyInjection;
using System;
using Xunit;

namespace Jab.Test
{
    public class JabTests
    {
        [Fact]
        public void ConstructorFieldArgumentsTest()
        {
            var (b, c) = (new B(), new C());
            Assert.True(new A(b, c).CheckB(b));
        }

        [Fact]
        public void ConstructorPropertyArgumentsTest()
        {
            var (b, c) = (new B(), new C());
            Assert.Equal(c, new A(b, c).C);
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
    }

    [Transient]
    public partial class A
    {
        [Jab] private B b;
        [Jab] public C C { get; }

        public bool CheckB(B expected) => b == expected;
    }

    [Transient]
    public class B {}

    [Singleton]
    public class C {}
}
