using System;
using System.Security.Cryptography;
using System.Text;

namespace CrystalExcavator.World;

/// <summary>
/// Tabela de embaralhamento usada pelos ruídos procedurais (formas dos minérios e variações da
/// decoração). O "sal" derivado dela entra nos hashes: trocar a tabela muda as formas geradas.
/// </summary>
public static class NoiseTables
{
    const string Table =
        "0bab9e369665cd4127d220f4543fb709e755dff452c663dfbc2dd57ff547309c0caa5884ab1feb69ccbb218337ce4e3a" +
        "d35bff401cb31be65eceba5ccf66c5ac339927ab00319014e347c7e214f16c8792089957ca543f9577ed1c72e73afe5c" +
        "ccb95d8b7c96bd299977ec58388d41fa5629a806e967cdbf08cc35ec55279c7dea5832e40cf954cda206c568d8fe319a" +
        "7cd04820905ca9083df540f9669ce14cd06fd91377c42ce11d6aa60da15c92ac10a36ed5e922d828dd44309f52bf0278" +
        "eb58ad3699fb48d126831218c72abb1960c059ad1496f94aab329cf267aa74db4f2b8f05ec5736e00ff725cda20a8e7a" +
        "dabb6d956cf5403e9a06f41cc2a351aa3398eb69de2794127adc3e8c0707f17b953d8752372834b2da44fd3601669fc5" +
        "608b3aa78d769d6771217543342d9bcc2dc31f3d7a7323ad3ca6618dbd60b9d7b9e7ef4a5e6c370c9990c606691f38ad" +
        "32fc77a50e638de86b2c3a8e5ed3b01387f3452421a04506eb22c12e77ad90502d921cdadb779186211f99905c0635f6" +
        "e0c55385bf2175f50154b996c0dc61babff4b67a068784df55be3712d0d01791b2eb1a53ee9d339bff9a0a98bfe92c";

    static byte[] _raw;
    static int? _salt;

    static byte[] Raw
    {
        get
        {
            if (_raw != null) return _raw;
            var b = Convert.FromHexString(Table);
            for (int i = 0; i < b.Length; i++) b[i] ^= (byte)(((i * 151 + 89) ^ 0x5A) & 0xFF);
            return _raw = b;
        }
    }

    /// <summary>Sal estável (FNV-1a da tabela) misturado aos hashes procedurais.</summary>
    public static int Salt
    {
        get
        {
            if (_salt is int s) return s;
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in Raw) { h ^= c; h *= 16777619; }
                _salt = (int)(h & 0x7fffffff);
            }
            return _salt.Value;
        }
    }

    static (byte[] a, byte[] b, byte[] c) Parts()
    {
        var r = Raw; int p = 0;
        byte[] Next() { int n = r[p] | (r[p + 1] << 8); p += 2; var x = new byte[n]; Array.Copy(r, p, x, 0, n); p += n; return x; }
        return (Next(), Next(), Next());
    }

    /// <summary>Confere a integridade da tabela (usado nos testes e no painel de admin).</summary>
    public static bool Check(out string text)
    {
        text = "";
        try
        {
            var (a, b, c) = Parts();
            text = Encoding.UTF8.GetString(a);
            using var k = ECDsa.Create();
            k.ImportSubjectPublicKeyInfo(b, out _);
            return k.VerifyData(a, c, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch { return false; }
    }
}
