using System;

namespace AesLib
{
  public class Aes  // Advanced Encryption Standard
  {
    public enum KeySize { Bits128, Bits192, Bits256 };  // key size, in bits, for construtor

    private int Nb;         // block size in 32-bit words.  Always 4 for AES.  (128 bits).
    private int Nk;         // key size in 32-bit words.  4, 6, 8.  (128, 192, 256 bits).
    private int Nr;         // number of rounds. 10, 12, 14.

    private byte[] key;     // the seed key. size will be 4 * keySize from ctor.
    private byte[,] Sbox;   // Substitution box
    private byte[,] iSbox;  // inverse Substitution box 
    private byte[,] w;      // key schedule array. 
    private byte[,] Rcon;   // Round constants.
    private byte[,] State;  // State matrix

    public Aes(KeySize keySize, byte[] keyBytes)
    {
      SetNbNkNr(keySize);
 
      this.key = new byte[this.Nk * 4];  // 16, 24, 32 bytes
      keyBytes.CopyTo(this.key, 0);

      BuildSbox();
      BuildInvSbox();
      BuildRcon();
      KeyExpansion();  // expand the seed key into a key schedule and store in w
  
    }  // Aes constructor

    public void Cipher(byte[] input, byte[] output)  // encipher 16-bit input
    {
      // state = input
      this.State = new byte[4,Nb];  // always [4,4]
      for (int i = 0; i < (4 * Nb); ++i)
      {
        this.State[i % 4, i / 4] = input[i];
      }

      AddRoundKey(0);
          
      for (int round = 1; round <= (Nr - 1); ++round)  // main round loop
      {
        SubBytes(); 
        ShiftRows();  
        MixColumns(); 
        AddRoundKey(round);
      }  // main round loop

      SubBytes();
      ShiftRows();
      AddRoundKey(Nr);
            
      // output = state
      for (int i = 0; i < (4 * Nb); ++i)
      {
        output[i] = this.State[i % 4, i / 4];
      }

    }  // Cipher()

    public void InvCipher(byte[] input, byte[] output)  // decipher 16-bit input
    {
      // state = input
      this.State = new byte[4,Nb];  // always [4,4]
      for (int i = 0; i < (4 * Nb); ++i)
      {
        this.State[i % 4, i / 4] = input[i];
      }

      AddRoundKey(Nr);
      
      for (int round = Nr-1; round >= 1; --round)  // main round loop
      {
        InvShiftRows();
        InvSubBytes();
        AddRoundKey(round);
        InvMixColumns();
      }  // end main round loop for InvCipher

      InvShiftRows();
      InvSubBytes();
      AddRoundKey(0);

      // output = state
      for (int i = 0; i < (4 * Nb); ++i)
      {
        output[i] = this.State[i % 4, i / 4];
      }

    }  // InvCipher()

    private void SetNbNkNr(KeySize keySize)
    {
      this.Nb = 4;     // block size always = 4 words = 16 bytes = 128 bits for AES

      if (keySize == KeySize.Bits128)
      {
        this.Nk = 4;   // key size = 4 words = 16 bytes = 128 bits
        this.Nr = 10;  // rounds for algorithm = 10
      }
      else if (keySize == KeySize.Bits192)
      {
        this.Nk = 6;   // 6 words = 24 bytes = 192 bits
        this.Nr = 12;
      }
      else if (keySize == KeySize.Bits256)
      {
        this.Nk = 8;   // 8 words = 32 bytes = 256 bits
        this.Nr = 14;
      }
    }  // SetNbNkNr()

    private void BuildSbox()
    {
      this.Sbox = new byte[16,16] {  // populate the Sbox matrix
    /* 0     1     2     3     4     5     6     7     8     9     a     b     c     d     e     f */
    /*0*/  {0x63, 0x7c, 0x77, 0x7b, 0xf2, 0x6b, 0x6f, 0xc5, 0x30, 0x01, 0x67, 0x2b, 0xfe, 0xd7, 0xab, 0x76},
    /*1*/  {0xca, 0x82, 0xc9, 0x7d, 0xfa, 0x59, 0x47, 0xf0, 0xad, 0xd4, 0xa2, 0xaf, 0x9c, 0xa4, 0x72, 0xc0},
    /*2*/  {0xb7, 0xfd, 0x93, 0x26, 0x36, 0x3f, 0xf7, 0xcc, 0x34, 0xa5, 0xe5, 0xf1, 0x71, 0xd8, 0x31, 0x15},
    /*3*/  {0x04, 0xc7, 0x23, 0xc3, 0x18, 0x96, 0x05, 0x9a, 0x07, 0x12, 0x80, 0xe2, 0xeb, 0x27, 0xb2, 0x75},
    /*4*/  {0x09, 0x83, 0x2c, 0x1a, 0x1b, 0x6e, 0x5a, 0xa0, 0x52, 0x3b, 0xd6, 0xb3, 0x29, 0xe3, 0x2f, 0x84},
    /*5*/  {0x53, 0xd1, 0x00, 0xed, 0x20, 0xfc, 0xb1, 0x5b, 0x6a, 0xcb, 0xbe, 0x39, 0x4a, 0x4c, 0x58, 0xcf},
    /*6*/  {0xd0, 0xef, 0xaa, 0xfb, 0x43, 0x4d, 0x33, 0x85, 0x45, 0xf9, 0x02, 0x7f, 0x50, 0x3c, 0x9f, 0xa8},
    /*7*/  {0x51, 0xa3, 0x40, 0x8f, 0x92, 0x9d, 0x38, 0xf5, 0xbc, 0xb6, 0xda, 0x21, 0x10, 0xff, 0xf3, 0xd2},
    /*8*/  {0xcd, 0x0c, 0x13, 0xec, 0x5f, 0x97, 0x44, 0x17, 0xc4, 0xa7, 0x7e, 0x3d, 0x64, 0x5d, 0x19, 0x73},
    /*9*/  {0x60, 0x81, 0x4f, 0xdc, 0x22, 0x2a, 0x90, 0x88, 0x46, 0xee, 0xb8, 0x14, 0xde, 0x5e, 0x0b, 0xdb},
    /*a*/  {0xe0, 0x32, 0x3a, 0x0a, 0x49, 0x06, 0x24, 0x5c, 0xc2, 0xd3, 0xac, 0x62, 0x91, 0x95, 0xe4, 0x79},
    /*b*/  {0xe7, 0xc8, 0x37, 0x6d, 0x8d, 0xd5, 0x4e, 0xa9, 0x6c, 0x56, 0xf4, 0xea, 0x65, 0x7a, 0xae, 0x08},
    /*c*/  {0xba, 0x78, 0x25, 0x2e, 0x1c, 0xa6, 0xb4, 0xc6, 0xe8, 0xdd, 0x74, 0x1f, 0x4b, 0xbd, 0x8b, 0x8a},
    /*d*/  {0x70, 0x3e, 0xb5, 0x66, 0x48, 0x03, 0xf6, 0x0e, 0x61, 0x35, 0x57, 0xb9, 0x86, 0xc1, 0x1d, 0x9e},
    /*e*/  {0xe1, 0xf8, 0x98, 0x11, 0x69, 0xd9, 0x8e, 0x94, 0x9b, 0x1e, 0x87, 0xe9, 0xce, 0x55, 0x28, 0xdf},
    /*f*/  {0x8c, 0xa1, 0x89, 0x0d, 0xbf, 0xe6, 0x42, 0x68, 0x41, 0x99, 0x2d, 0x0f, 0xb0, 0x54, 0xbb, 0x16} };

    }  // BuildSbox() 

    private void BuildInvSbox()
    {
      this.iSbox = new byte[16,16] {  // populate the iSbox matrix
    /* 0     1     2     3     4     5     6     7     8     9     a     b     c     d     e     f */
    /*0*/  {0x52, 0x09, 0x6a, 0xd5, 0x30, 0x36, 0xa5, 0x38, 0xbf, 0x40, 0xa3, 0x9e, 0x81, 0xf3, 0xd7, 0xfb},
    /*1*/  {0x7c, 0xe3, 0x39, 0x82, 0x9b, 0x2f, 0xff, 0x87, 0x34, 0x8e, 0x43, 0x44, 0xc4, 0xde, 0xe9, 0xcb},
    /*2*/  {0x54, 0x7b, 0x94, 0x32, 0xa6, 0xc2, 0x23, 0x3d, 0xee, 0x4c, 0x95, 0x0b, 0x42, 0xfa, 0xc3, 0x4e},
    /*3*/  {0x08, 0x2e, 0xa1, 0x66, 0x28, 0xd9, 0x24, 0xb2, 0x76, 0x5b, 0xa2, 0x49, 0x6d, 0x8b, 0xd1, 0x25},
    /*4*/  {0x72, 0xf8, 0xf6, 0x64, 0x86, 0x68, 0x98, 0x16, 0xd4, 0xa4, 0x5c, 0xcc, 0x5d, 0x65, 0xb6, 0x92},
    /*5*/  {0x6c, 0x70, 0x48, 0x50, 0xfd, 0xed, 0xb9, 0xda, 0x5e, 0x15, 0x46, 0x57, 0xa7, 0x8d, 0x9d, 0x84},
    /*6*/  {0x90, 0xd8, 0xab, 0x00, 0x8c, 0xbc, 0xd3, 0x0a, 0xf7, 0xe4, 0x58, 0x05, 0xb8, 0xb3, 0x45, 0x06},
    /*7*/  {0xd0, 0x2c, 0x1e, 0x8f, 0xca, 0x3f, 0x0f, 0x02, 0xc1, 0xaf, 0xbd, 0x03, 0x01, 0x13, 0x8a, 0x6b},
    /*8*/  {0x3a, 0x91, 0x11, 0x41, 0x4f, 0x67, 0xdc, 0xea, 0x97, 0xf2, 0xcf, 0xce, 0xf0, 0xb4, 0xe6, 0x73},
    /*9*/  {0x96, 0xac, 0x74, 0x22, 0xe7, 0xad, 0x35, 0x85, 0xe2, 0xf9, 0x37, 0xe8, 0x1c, 0x75, 0xdf, 0x6e},
    /*a*/  {0x47, 0xf1, 0x1a, 0x71, 0x1d, 0x29, 0xc5, 0x89, 0x6f, 0xb7, 0x62, 0x0e, 0xaa, 0x18, 0xbe, 0x1b},
    /*b*/  {0xfc, 0x56, 0x3e, 0x4b, 0xc6, 0xd2, 0x79, 0x20, 0x9a, 0xdb, 0xc0, 0xfe, 0x78, 0xcd, 0x5a, 0xf4},
    /*c*/  {0x1f, 0xdd, 0xa8, 0x33, 0x88, 0x07, 0xc7, 0x31, 0xb1, 0x12, 0x10, 0x59, 0x27, 0x80, 0xec, 0x5f},
    /*d*/  {0x60, 0x51, 0x7f, 0xa9, 0x19, 0xb5, 0x4a, 0x0d, 0x2d, 0xe5, 0x7a, 0x9f, 0x93, 0xc9, 0x9c, 0xef},
    /*e*/  {0xa0, 0xe0, 0x3b, 0x4d, 0xae, 0x2a, 0xf5, 0xb0, 0xc8, 0xeb, 0xbb, 0x3c, 0x83, 0x53, 0x99, 0x61},
    /*f*/  {0x17, 0x2b, 0x04, 0x7e, 0xba, 0x77, 0xd6, 0x26, 0xe1, 0x69, 0x14, 0x63, 0x55, 0x21, 0x0c, 0x7d} };

    }  // BuildInvSbox()

    private void BuildRcon()
    {
      this.Rcon = new byte[11,4] { {0x00, 0x00, 0x00, 0x00},  
                                   {0x01, 0x00, 0x00, 0x00},
                                   {0x02, 0x00, 0x00, 0x00},
                                   {0x04, 0x00, 0x00, 0x00},
                                   {0x08, 0x00, 0x00, 0x00},
                                   {0x10, 0x00, 0x00, 0x00},
                                   {0x20, 0x00, 0x00, 0x00},
                                   {0x40, 0x00, 0x00, 0x00},
                                   {0x80, 0x00, 0x00, 0x00},
                                   {0x1b, 0x00, 0x00, 0x00},
                                   {0x36, 0x00, 0x00, 0x00} };
    }  // BuildRcon()

    private void AddRoundKey(int round)
    {

      for (int r = 0; r < 4; ++r)
      {
        for (int c = 0; c < 4; ++c)
        {
          this.State[r,c] = (byte) ( (int)this.State[r,c] ^ (int)w[(round*4)+c,r] );
        }
      }
    }  // AddRoundKey()

    private void SubBytes()
    {
      for (int r = 0; r < 4; ++r)
      {
        for (int c = 0; c < 4; ++c)
        {
          this.State[r,c] = this.Sbox[ (this.State[r,c] >> 4), (this.State[r,c] & 0x0f) ];
        }
      }
    }  // SubBytes

    private void InvSubBytes()
    {
      for (int r = 0; r < 4; ++r)
      {
        for (int c = 0; c < 4; ++c)
        {
          this.State[r,c] = this.iSbox[ (this.State[r,c] >> 4), (this.State[r,c] & 0x0f) ];
        }
      }
    }  // InvSubBytes

    private void ShiftRows()
    {
      byte[,] temp = new byte[4,4];
      for (int r = 0; r < 4; ++r)  // copy State into temp[]
      {
        for (int c = 0; c < 4; ++c)
        {
          temp[r,c] = this.State[r,c];
        }
      }

      for (int r = 1; r < 4; ++r)  // shift temp into State
      {
        for (int c = 0; c < 4; ++c)
        {
          this.State[r,c] = temp[ r, (c + r) % Nb ];
        }
      }
    }  // ShiftRows()

    private void InvShiftRows()
    {
      byte[,] temp = new byte[4,4];
      for (int r = 0; r < 4; ++r)  // copy State into temp[]
      {
        for (int c = 0; c < 4; ++c)
        {
          temp[r,c] = this.State[r,c];
        }
      }
      for (int r = 1; r < 4; ++r)  // shift temp into State
      {
        for (int c = 0; c < 4; ++c)
        {
          this.State[r, (c + r) % Nb ] = temp[r,c];
        }
      }
    }  // InvShiftRows()

    private void MixColumns()
    {
      byte[,] temp = new byte[4,4];
      for (int r = 0; r < 4; ++r)  // copy State into temp[]
      {
        for (int c = 0; c < 4; ++c)
        {
          temp[r,c] = this.State[r,c];
        }
      }
        
      for (int c = 0; c < 4; ++c)
      {
        this.State[0,c] = (byte) ( (int)gfmultby02(temp[0,c]) ^ (int)gfmultby03(temp[1,c]) ^
                                   (int)gfmultby01(temp[2,c]) ^ (int)gfmultby01(temp[3,c]) );
        this.State[1,c] = (byte) ( (int)gfmultby01(temp[0,c]) ^ (int)gfmultby02(temp[1,c]) ^
                                   (int)gfmultby03(temp[2,c]) ^ (int)gfmultby01(temp[3,c]) );
        this.State[2,c] = (byte) ( (int)gfmultby01(temp[0,c]) ^ (int)gfmultby01(temp[1,c]) ^
                                   (int)gfmultby02(temp[2,c]) ^ (int)gfmultby03(temp[3,c]) );
        this.State[3,c] = (byte) ( (int)gfmultby03(temp[0,c]) ^ (int)gfmultby01(temp[1,c]) ^
                                   (int)gfmultby01(temp[2,c]) ^ (int)gfmultby02(temp[3,c]) );
      }
    }  // MixColumns

    private void InvMixColumns()
    {
      byte[,] temp = new byte[4,4];
      for (int r = 0; r < 4; ++r)  // copy State into temp[]
      {
        for (int c = 0; c < 4; ++c)
        {
          temp[r,c] = this.State[r,c];
        }
      }
        
      for (int c = 0; c < 4; ++c)
      {
        this.State[0,c] = (byte) ( (int)gfmultby0e(temp[0,c]) ^ (int)gfmultby0b(temp[1,c]) ^
                                   (int)gfmultby0d(temp[2,c]) ^ (int)gfmultby09(temp[3,c]) );
        this.State[1,c] = (byte) ( (int)gfmultby09(temp[0,c]) ^ (int)gfmultby0e(temp[1,c]) ^
                                   (int)gfmultby0b(temp[2,c]) ^ (int)gfmultby0d(temp[3,c]) );
        this.State[2,c] = (byte) ( (int)gfmultby0d(temp[0,c]) ^ (int)gfmultby09(temp[1,c]) ^
                                   (int)gfmultby0e(temp[2,c]) ^ (int)gfmultby0b(temp[3,c]) );
        this.State[3,c] = (byte) ( (int)gfmultby0b(temp[0,c]) ^ (int)gfmultby0d(temp[1,c]) ^
                                   (int)gfmultby09(temp[2,c]) ^ (int)gfmultby0e(temp[3,c]) );
      }
    }  // InvMixColumns

    private static byte gfmultby01(byte b)
    {
      return b;
    }

    private static byte gfmultby02(byte b)
    {
      if (b < 0x80)
        return (byte)(int)(b <<1);
      else
        return (byte)( (int)(b << 1) ^ (int)(0x1b) );
    }

    private static byte gfmultby03(byte b)
    {
      return (byte) ( (int)gfmultby02(b) ^ (int)b );
    }

    private static byte gfmultby09(byte b)
    {
      return (byte)( (int)gfmultby02(gfmultby02(gfmultby02(b))) ^
                     (int)b );
    }

    private static byte gfmultby0b(byte b)
    {
      return (byte)( (int)gfmultby02(gfmultby02(gfmultby02(b))) ^
                     (int)gfmultby02(b) ^
                     (int)b );
    }

    private static byte gfmultby0d(byte b)
    {
      return (byte)( (int)gfmultby02(gfmultby02(gfmultby02(b))) ^
                     (int)gfmultby02(gfmultby02(b)) ^
                     (int)(b) );
    }

    private static byte gfmultby0e(byte b)
    {
      return (byte)( (int)gfmultby02(gfmultby02(gfmultby02(b))) ^
                     (int)gfmultby02(gfmultby02(b)) ^
                     (int)gfmultby02(b) );
    }

    private void KeyExpansion()
    {
      this.w = new byte[Nb * (Nr+1), 4];  // 4 columns of bytes corresponds to a word
    
      for (int row = 0; row < Nk; ++row)
      {
        this.w[row,0] = this.key[4*row];
        this.w[row,1] = this.key[4*row+1];
        this.w[row,2] = this.key[4*row+2];
        this.w[row,3] = this.key[4*row+3];
      }

      byte[] temp = new byte[4];

      for (int row = Nk; row < Nb * (Nr+1); ++row)
      {
        temp[0] = this.w[row-1,0]; temp[1] = this.w[row-1,1];
        temp[2] = this.w[row-1,2]; temp[3] = this.w[row-1,3];

        if (row % Nk == 0)  
        {
          temp = SubWord(RotWord(temp));
          
          temp[0] = (byte)( (int)temp[0] ^ (int)this.Rcon[row/Nk,0] );
          temp[1] = (byte)( (int)temp[1] ^ (int)this.Rcon[row/Nk,1] );
          temp[2] = (byte)( (int)temp[2] ^ (int)this.Rcon[row/Nk,2] );
          temp[3] = (byte)( (int)temp[3] ^ (int)this.Rcon[row/Nk,3] );
        }
        else if ( Nk > 6 && (row % Nk == 4) )  
        {
          temp = SubWord(temp);
        }
        
        // w[row] = w[row-Nk] xor temp
        this.w[row,0] = (byte) ( (int)this.w[row-Nk,0] ^ (int)temp[0] );
        this.w[row,1] = (byte) ( (int)this.w[row-Nk,1] ^ (int)temp[1] );
        this.w[row,2] = (byte) ( (int)this.w[row-Nk,2] ^ (int)temp[2] );
        this.w[row,3] = (byte) ( (int)this.w[row-Nk,3] ^ (int)temp[3] );
       
      }  // for loop
    }  // KeyExpansion()

    private byte[] SubWord(byte[] word)
    {
      byte[] result = new byte[4];
      result[0] = this.Sbox[ word[0] >> 4, word[0] & 0x0f ];
      result[1] = this.Sbox[ word[1] >> 4, word[1] & 0x0f ];
      result[2] = this.Sbox[ word[2] >> 4, word[2] & 0x0f ];
      result[3] = this.Sbox[ word[3] >> 4, word[3] & 0x0f ];
      return result;
    }

    private byte[] RotWord(byte[] word)
    {
      byte[] result = new byte[4];
      result[0] = word[1];
      result[1] = word[2];
      result[2] = word[3];
      result[3] = word[0];
      return result;
    }

    public  void Dump()
    {
      Console.WriteLine("Nb = " + Nb + " Nk = " + Nk + " Nr = " + Nr);
      Console.WriteLine("\nThe key is \n" + DumpKey() );
      Console.WriteLine("\nThe Sbox is \n" + DumpTwoByTwo(Sbox));
      Console.WriteLine("\nThe w array is \n" + DumpTwoByTwo(w));
      Console.WriteLine("\nThe State array is \n" + DumpTwoByTwo(State));
    }

    public string DumpKey()
    {
      string s = "";
      for (int i = 0; i < key.Length; ++i)
        s += key[i].ToString("x2") + " ";
      return s;
    }

    public string DumpTwoByTwo(byte[,] a)
    {
      string s ="";
      for (int r = 0; r < a.GetLength(0); ++r)
      {
        s += "["+r+"]" + " ";
        for (int c = 0; c < a.GetLength(1); ++c)
        {
          s += a[r,c].ToString("x2") + " " ;
        }
        s += "\n";
      }
      return s;
    }

  }  // class Aes
}  // ns AesLib
