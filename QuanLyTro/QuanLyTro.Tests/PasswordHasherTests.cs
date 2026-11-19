using QuanLyTro.Server.Security;

namespace QuanLyTro.Tests;

[TestClass]
public class PasswordHasherTests
{
    [TestMethod]
    public void Hash_Twice_ProducesDifferentSalts()
    {
        var h1 = PasswordHasher.Hash("mk123456");
        var h2 = PasswordHasher.Hash("mk123456");
        Assert.AreNotEqual(h1, h2);
        StringAssert.StartsWith(h1, "PBKDF2$600000$");
    }

    [TestMethod]
    public void Verify_CorrectAndWrongPassword()
    {
        var stored = PasswordHasher.Hash("mk123456");
        Assert.IsTrue(PasswordHasher.Verify("mk123456", stored));
        Assert.IsFalse(PasswordHasher.Verify("MK123456", stored));
        Assert.IsFalse(PasswordHasher.Verify("mk123456", "gibberish"));
    }
}
