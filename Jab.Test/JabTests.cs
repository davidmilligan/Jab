using System;
using Xunit;

namespace Jab
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
    }

    public partial class A
    {
        [Jab] private B b;
        [Jab] public C C { get; }

        public bool CheckB(B expected) => b == expected;
    }

    public class B {}

    public class C {}
}
