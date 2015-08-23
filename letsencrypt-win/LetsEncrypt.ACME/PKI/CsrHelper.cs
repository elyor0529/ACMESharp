﻿using Newtonsoft.Json;
using OpenSSL.Core;
using OpenSSL.Crypto;
using OpenSSL.X509;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetsEncrypt.ACME.PKI
{
    public class CsrHelper
    {
        public static readonly BigNumber E_3 = 3;
        public static readonly BigNumber E_F4 = 0x10001;

        public const int CSR_FORMAT_PEM = 0;
        public const int CSR_FORMAT_DER = 1;
        public const int CSR_FORMAT_PRINT = 2;

        public delegate int RsaKeyGeneratorCallback(int p, int n, object cbArg);

        public static RsaKeyPair GenerateRsaPrivateKey(int bits = 2048, BigNumber e = null,
                RsaKeyGeneratorCallback cb = null, object cbArg = null)
        {
            if (e == null)
                e = E_F4;

            using (var rsa = new RSA())
            {
                BigNumber.GeneratorHandler cbWrapper = null;
                if (cb != null)
                    cbWrapper = (x,y,z) => cb(x,y,z);

                Cipher enc = null;
                string pwd = null;
                PasswordHandler pwdCb = null;
                // If we choose to encrypt:
                //      Cipher.DES_CBC;
                //      Cipher.DES_EDE3_CBC;
                //      Cipher.Idea_CBC;
                //      Cipher.AES_128_CBC;
                //      Cipher.AES_192_CBC;
                //      Cipher.AES_256_CBC;
                //   and pwd != null || pwdCb != null
                // We can use a pwdCb to get a password interactively or we can
                // simply pass in a fixed password string (no cbPwd, just pwd)
                if (pwd != null)
                    pwdCb = DefaultPasswordHandler;

                // Ref:  http://openssl.org/docs/manmaster/crypto/RSA_generate_key_ex.html
                rsa.GenerateKeys(bits, e, cbWrapper, cbArg);

                using (var bio = BIO.MemoryBuffer())
                {
                    // Ref:  http://openssl.org/docs/manmaster/crypto/PEM_write_bio_RSAPrivateKey.html
                    rsa.WritePrivateKey(bio, enc, pwdCb, pwd);
                    return new RsaKeyPair(bits, e.ToHexString(), bio.ReadString());
                }
            }
        }

        public static Csr GenerateCsr(CsrDetails csrDetails, RsaKeyPair rsaKeyPair, string messageDigest = "SHA256")
        {
            var rsaKeys = CryptoKey.FromPrivateKey(rsaKeyPair.Pem, null);

            // Translate from our external form to our OpenSSL internal form
            // Ref:  https://www.openssl.org/docs/manmaster/crypto/X509_NAME_new.html
            var xn = new X509Name
            {
                Common =             /**/ csrDetails.CommonName,       // CN
                Country =            /**/ csrDetails.Country,          // C
                StateOrProvince =    /**/ csrDetails.StateOrProvince,  // ST
                Locality =           /**/ csrDetails.Locality,         // L
                Organization =       /**/ csrDetails.Organization,     // O
                OrganizationUnit =   /**/ csrDetails.OrganizationUnit, // OU
                Description =        /**/ csrDetails.Description,      // D
                Surname =            /**/ csrDetails.Surname,          // S
                Given =              /**/ csrDetails.GivenName,        // G
                Initials =           /**/ csrDetails.Initials,         // I
                Title =              /**/ csrDetails.Title,            // T
                SerialNumber =       /**/ csrDetails.SerialNumber,     // SN
                UniqueIdentifier =   /**/ csrDetails.UniqueIdentifier, // UID
            };

            var xr = new X509Request(0, xn, rsaKeys);
            var md = MessageDigest.CreateByName(messageDigest); ;
            xr.Sign(rsaKeys, md);
            using (var bio = BIO.MemoryBuffer())
            {
                xr.Write(bio);
                return new Csr(bio.ReadString());
            }
        }

        public static int DefaultRsaKeyGeneratorCallback(int p, int n, object cbArg)
        {
            var cout = cbArg as TextWriter;
            if (cout == null)
                cout = Console.Error;

            switch (p)
            {
                case 0: cout.Write('.'); break;
                case 1: cout.Write('+'); break;
                case 2: cout.Write('*'); break;
                case 3: cout.WriteLine(); break;
            }

            return 1;
        }


        private static string DefaultPasswordHandler(bool verify, object arg)
        {
            var passout = arg as string;

            if (!string.IsNullOrEmpty(passout))
                return File.ReadAllText(passout);

            while (true)
            {
                Console.Error.Write("Enter pass phrase:");
                var strPassword = ReadPassword();
                Console.Error.WriteLine();

                if (strPassword.Length == 0)
                    continue;

                if (!verify)
                    return strPassword;

                Console.Error.Write("Verifying - Enter pass phrase:");
                var strVerify = ReadPassword();
                Console.Error.WriteLine();

                if (strPassword == strVerify)
                    return strPassword;

                Console.Error.WriteLine("Passwords don't match, try again.");
            }
        }

        private static string ReadPassword()
        {
            Console.TreatControlCAsInput = true;
            var sb = new StringBuilder();

            while (true)
            {
                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Enter)
                    break;

                if (key.Key == ConsoleKey.C && key.Modifiers == ConsoleModifiers.Control)
                {
                    Console.Error.WriteLine();
                    throw new Exception("Canceled");
                }

                sb.Append(key.KeyChar);
            }

            Console.TreatControlCAsInput = false;

            return sb.ToString();
        }

        public class RsaKeyPair
        {
            public RsaKeyPair(int bits, string e, string pem)
            {
                Bits = bits;
                E = e;
                Pem = pem;
            }

            public int Bits
            { get; private set; }

            public string E
            { get; private set; }

            public object BigNumber
            { get; set; }

            public string Pem
            { get; private set; }

            public void Save(Stream s)
            {
                using (var w = new StreamWriter(s))
                {
                    JsonConvert.SerializeObject(this);
                }
            }

            public static RsaKeyPair Load(Stream s)
            {
                using (var r = new StreamReader(s))
                {
                    return JsonConvert.DeserializeObject<RsaKeyPair>(r.ReadToEnd());
                }
            }
        }

        public class CsrDetails
        {
            /// <summary>X509 'CN'</summary>
            public string CommonName { get; set; }

            /// <summary>X509 'C'</summary>
            public string Country { get; set; }

            /// <summary>X509 'ST'</summary>
            public string StateOrProvince { get; set; }

            /// <summary>X509 'L'</summary>
            public string Locality { get; set; }

            /// <summary>X509 'O'</summary>
            public string Organization { get; set; }

            /// <summary>X509 'OU'</summary>
            public string OrganizationUnit { get; set; }

            /// <summary>X509 'D'</summary>
            public string Description { get; set; }

            /// <summary>X509 'S'</summary>
            public string Surname { get; set; }

            /// <summary>X509 'G'</summary>
            public string GivenName { get; set; }

            /// <summary>X509 'I'</summary>
            public string Initials { get; set; }

            /// <summary>X509 'T'</summary>
            public string Title { get; set; }

            /// <summary>X509 'SN'</summary>
            public string SerialNumber { get; set; }

            /// <summary>X509 'UID'</summary>
            public string UniqueIdentifier { get; set; }

            public void Save(Stream s)
            {
                using (var w = new StreamWriter(s))
                {
                    w.Write(JsonConvert.SerializeObject(this));
                }
            }

            public static Csr Load(Stream s)
            {
                using (var r = new StreamReader(s))
                {
                    return JsonConvert.DeserializeObject<Csr>(r.ReadToEnd());
                }
            }
        }

        public class Csr
        {
            public Csr(string pem)
            {
                Pem = pem;
            }

            public string Pem
            { get; private set; }

            public void ExportAsDer(Stream s)
            {
                var xr = new X509Request(Pem);
                using (var bio = BIO.MemoryBuffer())
                {
                    xr.Write_DER(bio);
                    var arr = bio.ReadBytes((int)bio.BytesPending);
                    s.Write(arr.Array, arr.Offset, arr.Count);
                }
            }

            public void Save(Stream s)
            {
                using (var w = new StreamWriter(s))
                {
                    w.Write(JsonConvert.SerializeObject(this));
                }
            }

            public static Csr Load(Stream s)
            {
                using (var r = new StreamReader(s))
                {
                    return JsonConvert.DeserializeObject<Csr>(r.ReadToEnd());
                }
            }
        }
    }
}
