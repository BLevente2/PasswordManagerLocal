namespace PasswordManagerLocalBackend.Constants;

public static class DatabaseConstants
{
    public const int CurrentDbVersion = 1;
    public const int OldestSupportedDbVersion = 1;
    public const int DbConfigHeaderLength = 16;
    public const int DbConfigMagicLength = 2;
    public const int DbConfigVersionOffset = 8;
    public const int DbConfigHeaderLengthOffset = 12;
    public const byte DbConfigMagicFirstByte = 0b00000110;
    public const byte DbConfigMagicSecondByte = 0b00000111;
}
