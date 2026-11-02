using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public static class ConnectionTokenUtils
{
    public static byte[] NewToken() => Guid.NewGuid().ToByteArray();

    public static int HashToken(byte[] token)
    {
        if (token == null || token.Length != 16)
        {
            Debug.LogError("HashToken: invalid token (null or wrong length)");
            return 0;
        }
        return new Guid(token).GetHashCode();
    }

    public static string TokenToString(byte[] token)
    {
        if (token == null || token.Length != 16) return "InvalidToken";
        return new Guid(token).ToString();
    }
}
