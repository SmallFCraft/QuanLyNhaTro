namespace QuanLyTro.Shared.Models;

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

public class AuthorizationException : Exception
{
    public AuthorizationException() : base("Khong co quyen thuc hien hanh dong nay.") { }
    public AuthorizationException(string message) : base(message) { }
}

public class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException() : base("Ten dang nhap hoac mat khau khong dung.") { }
}

public class AccountLockedException : Exception
{
    public AccountLockedException(string message) : base(message) { }
}

public class NotFoundOrDeniedException : Exception
{
    public NotFoundOrDeniedException() : base("Khong tim thay du lieu hoac khong co quyen truy cap.") { }
    public NotFoundOrDeniedException(string message) : base(message) { }
}
