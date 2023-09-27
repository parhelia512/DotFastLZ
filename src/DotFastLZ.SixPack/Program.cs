﻿/*
  6PACK - file compressor using FastLZ (lightning-fast compression library)
  Copyright (C) 2007-2020 Ariya Hidayat <ariya.hidayat@gmail.com>
  Copyright (C) 2023 Choi Ikpil <ikpil@naver.com>

  Permission is hereby granted, free of charge, to any person obtaining a copy
  of this software and associated documentation files (the "Software"), to deal
  in the Software without restriction, including without limitation the rights
  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
  copies of the Software, and to permit persons to whom the Software is
  furnished to do so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in
  all copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
  THE SOFTWARE.
*/

using System;
using System.IO;

namespace DotFastLZ.SixPack;

public static class Program
{
    private const int SIXPACK_VERSION_MAJOR = 0;
    private const int SIXPACK_VERSION_MINOR = 1;
    private const int SIXPACK_VERSION_REVISION = 0;
    private const string SIXPACK_VERSION_STRING = "snapshot 20070615";

    /* magic identifier for 6pack file */
    private static readonly byte[] sixpack_magic = { 137, (byte)'6', (byte)'P', (byte)'K', 13, 10, 26, 10 };
    private const int BLOCK_SIZE = (2 * 64 * 1024);

    /* for Adler-32 checksum algorithm, see RFC 1950 Section 8.2 */
    private const int ADLER32_BASE = 65521;

    private static ulong Update_adler32(ulong checksum, byte[] buf, int len)
    {
        int ptr = 0;
        ulong s1 = checksum & 0xffff;
        ulong s2 = (checksum >> 16) & 0xffff;

        while (len > 0)
        {
            var k = len < 5552 ? len : 5552;
            len -= k;

            while (k >= 8)
            {
                s1 += buf[ptr++];
                s2 += s1;
                s1 += buf[ptr++];
                s2 += s1;
                s1 += buf[ptr++];
                s2 += s1;
                s1 += buf[ptr++];
                s2 += s1;
                s1 += buf[ptr++];
                s2 += s1;
                s1 += buf[ptr++];
                s2 += s1;
                s1 += buf[ptr++];
                s2 += s1;
                s1 += buf[ptr++];
                s2 += s1;
                k -= 8;
            }

            while (k-- > 0)
            {
                s1 += buf[ptr++];
                s2 += s1;
            }

            s1 = s1 % ADLER32_BASE;
            s2 = s2 % ADLER32_BASE;
        }

        return (s2 << 16) + s1;
    }

    private static void Usage()
    {
        Console.WriteLine("6pack: high-speed file compression tool");
        Console.WriteLine("Copyright (C) Ariya Hidayat");
        Console.WriteLine("Copyright (C) Choi Ikpil");
        Console.WriteLine("");
        Console.WriteLine("Usage: 6pack [options]  input-file  output-file");
        Console.WriteLine("");
        Console.WriteLine("Options:");
        Console.WriteLine("  -1    compress faster");
        Console.WriteLine("  -2    compress better");
        Console.WriteLine("  -v    show program version");
        Console.WriteLine("  -mem  check in-memory compression speed");
        Console.WriteLine("");
    }

    /* return non-zero if magic sequence is detected */
    /* warning: reset the read pointer to the beginning of the file */
    public static int detect_magic(FileStream f)
    {
        byte[] buffer = new byte[8];
        int bytes_read;
        int c;

        f.Seek(0, SeekOrigin.Begin);
        bytes_read = f.Read(buffer, 0, 8);
        f.Seek(0, SeekOrigin.Begin);
        if (bytes_read < 8)
        {
            return 0;
        }

        for (c = 0; c < 8; c++)
        {
            if (buffer[c] != sixpack_magic[c])
            {
                return 0;
            }
        }

        return -1;
    }


    public static int Main(string[] args)
    {
        Usage();

//
// void write_magic(FILE* f) { fwrite(sixpack_magic, 8, 1, f); }
//
// void write_chunk_header(FILE* f, int id, int options, unsigned long size, unsigned long checksum, unsigned long extra) {
//   unsigned char buffer[16];
//
//   buffer[0] = id & 255;
//   buffer[1] = id >> 8;
//   buffer[2] = options & 255;
//   buffer[3] = options >> 8;
//   buffer[4] = size & 255;
//   buffer[5] = (size >> 8) & 255;
//   buffer[6] = (size >> 16) & 255;
//   buffer[7] = (size >> 24) & 255;
//   buffer[8] = checksum & 255;
//   buffer[9] = (checksum >> 8) & 255;
//   buffer[10] = (checksum >> 16) & 255;
//   buffer[11] = (checksum >> 24) & 255;
//   buffer[12] = extra & 255;
//   buffer[13] = (extra >> 8) & 255;
//   buffer[14] = (extra >> 16) & 255;
//   buffer[15] = (extra >> 24) & 255;
//
//   fwrite(buffer, 16, 1, f);
// }
//
// int pack_file_compressed(const char* input_file, int method, int level, FILE* f) {
//   FILE* in;
//   unsigned long fsize;
//   unsigned long checksum;
//   const char* shown_name;
//   unsigned char buffer[BLOCK_SIZE];
//   unsigned char result[BLOCK_SIZE * 2]; /* FIXME twice is too large */
//   unsigned char progress[20];
//   int c;
//   unsigned long percent;
//   unsigned long total_read;
//   unsigned long total_compressed;
//   int chunk_size;
//
//   /* sanity check */
//   in = fopen(input_file, "rb");
//   if (!in) {
//     printf("Error: could not open %s\n", input_file);
//     return -1;
//   }
//
//   /* find size of the file */
//   fseek(in, 0, SEEK_END);
//   fsize = ftell(in);
//   fseek(in, 0, SEEK_SET);
//
//   /* already a 6pack archive? */
//   if (detect_magic(in)) {
//     printf("Error: file %s is already a 6pack archive!\n", input_file);
//     fclose(in);
//     return -1;
//   }
//
//   /* truncate directory prefix, e.g. "foo/bar/FILE.txt" becomes "FILE.txt" */
//   shown_name = input_file + strlen(input_file) - 1;
//   while (shown_name > input_file)
//     if (*(shown_name - 1) == PATH_SEPARATOR)
//       break;
//     else
//       shown_name--;
//
//   /* chunk for File Entry */
//   buffer[0] = fsize & 255;
//   buffer[1] = (fsize >> 8) & 255;
//   buffer[2] = (fsize >> 16) & 255;
//   buffer[3] = (fsize >> 24) & 255;
// #if 0
//   buffer[4] = (fsize >> 32) & 255;
//   buffer[5] = (fsize >> 40) & 255;
//   buffer[6] = (fsize >> 48) & 255;
//   buffer[7] = (fsize >> 56) & 255;
// #else
//   /* because fsize is only 32-bit */
//   buffer[4] = 0;
//   buffer[5] = 0;
//   buffer[6] = 0;
//   buffer[7] = 0;
// #endif
//   buffer[8] = (strlen(shown_name) + 1) & 255;
//   buffer[9] = (strlen(shown_name) + 1) >> 8;
//   checksum = 1L;
//   checksum = update_adler32(checksum, buffer, 10);
//   checksum = update_adler32(checksum, shown_name, strlen(shown_name) + 1);
//   write_chunk_header(f, 1, 0, 10 + strlen(shown_name) + 1, checksum, 0);
//   fwrite(buffer, 10, 1, f);
//   fwrite(shown_name, strlen(shown_name) + 1, 1, f);
//   total_compressed = 16 + 10 + strlen(shown_name) + 1;
//
//   /* for progress status */
//   memset(progress, ' ', 20);
//   if (strlen(shown_name) < 16)
//     for (c = 0; c < (int)strlen(shown_name); c++) progress[c] = shown_name[c];
//   else {
//     for (c = 0; c < 13; c++) progress[c] = shown_name[c];
//     progress[13] = '.';
//     progress[14] = '.';
//     progress[15] = ' ';
//   }
//   progress[16] = '[';
//   progress[17] = 0;
//   printf("%s", progress);
//   for (c = 0; c < 50; c++) printf(".");
//   printf("]\r");
//   printf("%s", progress);
//
//   /* read file and place in archive */
//   total_read = 0;
//   percent = 0;
//   for (;;) {
//     int compress_method = method;
//     int last_percent = (int)percent;
//     size_t bytes_read = fread(buffer, 1, BLOCK_SIZE, in);
//     if (bytes_read == 0) break;
//     total_read += bytes_read;
//
//     /* for progress */
//     if (fsize < (1 << 24))
//       percent = total_read * 100 / fsize;
//     else
//       percent = total_read / 256 * 100 / (fsize >> 8);
//     percent >>= 1;
//     while (last_percent < (int)percent) {
//       printf("#");
//       last_percent++;
//     }
//
//     /* too small, don't bother to compress */
//     if (bytes_read < 32) compress_method = 0;
//
//     /* write to output */
//     switch (compress_method) {
//       /* FastLZ */
//       case 1:
//         chunk_size = fastlz_compress_level(level, buffer, bytes_read, result);
//         checksum = update_adler32(1L, result, chunk_size);
//         write_chunk_header(f, 17, 1, chunk_size, checksum, bytes_read);
//         fwrite(result, 1, chunk_size, f);
//         total_compressed += 16;
//         total_compressed += chunk_size;
//         break;
//
//       /* uncompressed, also fallback method */
//       case 0:
//       default:
//         checksum = 1L;
//         checksum = update_adler32(checksum, buffer, bytes_read);
//         write_chunk_header(f, 17, 0, bytes_read, checksum, bytes_read);
//         fwrite(buffer, 1, bytes_read, f);
//         total_compressed += 16;
//         total_compressed += bytes_read;
//         break;
//     }
//   }
//
//   fclose(in);
//   if (total_read != fsize) {
//     printf("\n");
//     printf("Error: reading %s failed!\n", input_file);
//     return -1;
//   } else {
//     printf("] ");
//     if (total_compressed < fsize) {
//       if (fsize < (1 << 20))
//         percent = total_compressed * 1000 / fsize;
//       else
//         percent = total_compressed / 256 * 1000 / (fsize >> 8);
//       percent = 1000 - percent;
//       printf("%2d.%d%% saved", (int)percent / 10, (int)percent % 10);
//     }
//     printf("\n");
//   }
//
//   return 0;
// }
//
// int pack_file(int compress_level, const char* input_file, const char* output_file) {
//   FILE* f;
//   int result;
//
//   f = fopen(output_file, "rb");
//   if (f) {
//     fclose(f);
//     printf("Error: file %s already exists. Aborted.\n\n", output_file);
//     return -1;
//   }
//
//   f = fopen(output_file, "wb");
//   if (!f) {
//     printf("Error: could not create %s. Aborted.\n\n", output_file);
//     return -1;
//   }
//
//   write_magic(f);
//
//   result = pack_file_compressed(input_file, 1, compress_level, f);
//   fclose(f);
//
//   return result;
// }
//
// #ifdef SIXPACK_BENCHMARK_WIN32
// int benchmark_speed(int compress_level, const char* input_file);
//
// int benchmark_speed(int compress_level, const char* input_file) {
//   FILE* in;
//   unsigned long fsize;
//   unsigned long maxout;
//   const char* shown_name;
//   unsigned char* buffer;
//   unsigned char* result;
//   size_t bytes_read;
//
//   /* sanity check */
//   in = fopen(input_file, "rb");
//   if (!in) {
//     printf("Error: could not open %s\n", input_file);
//     return -1;
//   }
//
//   /* find size of the file */
//   fseek(in, 0, SEEK_END);
//   fsize = ftell(in);
//   fseek(in, 0, SEEK_SET);
//
//   /* already a 6pack archive? */
//   if (detect_magic(in)) {
//     printf("Error: no benchmark for 6pack archive!\n");
//     fclose(in);
//     return -1;
//   }
//
//   /* truncate directory prefix, e.g. "foo/bar/FILE.txt" becomes "FILE.txt" */
//   shown_name = input_file + strlen(input_file) - 1;
//   while (shown_name > input_file)
//     if (*(shown_name - 1) == PATH_SEPARATOR)
//       break;
//     else
//       shown_name--;
//
//   maxout = 1.05 * fsize;
//   maxout = (maxout < 66) ? 66 : maxout;
//   buffer = (unsigned char*)malloc(fsize);
//   result = (unsigned char*)malloc(maxout);
//   if (!buffer || !result) {
//     printf("Error: not enough memory!\n");
//     free(buffer);
//     free(result);
//     fclose(in);
//     return -1;
//   }
//
//   printf("Reading source file....\n");
//   bytes_read = fread(buffer, 1, fsize, in);
//   if (bytes_read != fsize) {
//     printf("Error reading file %s!\n", shown_name);
//     printf("Read %d bytes, expecting %d bytes\n", bytes_read, fsize);
//     free(buffer);
//     free(result);
//     fclose(in);
//     return -1;
//   }
//
//   /* shamelessly copied from QuickLZ 1.20 test program */
//   {
//     unsigned int j, y;
//     size_t i, u = 0;
//     double mbs, fastest;
//     unsigned long compressed_size;
//
//     printf("Setting HIGH_PRIORITY_CLASS...\n");
//     SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
//
//     printf("Benchmarking FastLZ Level %d, please wait...\n", compress_level);
//
//     i = bytes_read;
//     fastest = 0.0;
//     for (j = 0; j < 3; j++) {
//       y = 0;
//       mbs = GetTickCount();
//       while (GetTickCount() == mbs)
//         ;
//       mbs = GetTickCount();
//       while (GetTickCount() - mbs < 3000) /* 1% accuracy with 18.2 timer */
//       {
//         u = fastlz_compress_level(compress_level, buffer, bytes_read, result);
//         y++;
//       }
//
//       mbs = ((double)i * (double)y) / ((double)(GetTickCount() - mbs) / 1000.) / 1000000.;
//       /*printf(" %.1f Mbyte/s  ", mbs);*/
//       if (fastest < mbs) fastest = mbs;
//     }
//
//     printf("\nCompressed %d bytes into %d bytes (%.1f%%) at %.1f Mbyte/s.\n", (unsigned int)i, (unsigned int)u,
//            (double)u / (double)i * 100., fastest);
//
// #if 1
//     fastest = 0.0;
//     compressed_size = u;
//     for (j = 0; j < 3; j++) {
//       y = 0;
//       mbs = GetTickCount();
//       while (GetTickCount() == mbs)
//         ;
//       mbs = GetTickCount();
//       while (GetTickCount() - mbs < 3000) /* 1% accuracy with 18.2 timer */
//       {
//         u = fastlz_decompress(result, compressed_size, buffer, bytes_read);
//         y++;
//       }
//
//       mbs = ((double)i * (double)y) / ((double)(GetTickCount() - mbs) / 1000.) / 1000000.;
//       /*printf(" %.1f Mbyte/s  ", mbs);*/
//       if (fastest < mbs) fastest = mbs;
//     }
//
//     printf("\nDecompressed at %.1f Mbyte/s.\n\n(1 MB = 1000000 byte)\n", fastest);
// #endif
//   }
//
//   fclose(in);
//   return 0;
// }
// #endif /* SIXPACK_BENCHMARK_WIN32 */
//
// int main(int argc, char** argv) {
//   int i;
//   int compress_level;
//   int benchmark;
//   char* input_file;
//   char* output_file;
//
//   /* show help with no argument at all*/
//   if (argc == 1) {
//     usage();
//     return 0;
//   }
//
//   /* default compression level, not the fastest */
//   compress_level = 2;
//
//   /* do benchmark only when explicitly specified */
//   benchmark = 0;
//
//   /* no file is specified */
//   input_file = 0;
//   output_file = 0;
//
//   for (i = 1; i <= argc; i++) {
//     char* argument = argv[i];
//
//     if (!argument) continue;
//
//     /* display help on usage */
//     if (!strcmp(argument, "-h") || !strcmp(argument, "--help")) {
//       usage();
//       return 0;
//     }
//
//     /* check for version information */
//     if (!strcmp(argument, "-v") || !strcmp(argument, "--version")) {
//       printf("6pack: high-speed file compression tool\n");
//       printf("Version %s (using FastLZ %s)\n", SIXPACK_VERSION_STRING, FASTLZ_VERSION_STRING);
//       printf("Copyright (C) Ariya Hidayat\n");
//       printf("\n");
//       return 0;
//     }
//
//     /* test compression speed? */
//     if (!strcmp(argument, "-mem")) {
//       benchmark = 1;
//       continue;
//     }
//
//     /* compression level */
//     if (!strcmp(argument, "-1") || !strcmp(argument, "--fastest")) {
//       compress_level = 1;
//       continue;
//     }
//     if (!strcmp(argument, "-2")) {
//       compress_level = 2;
//       continue;
//     }
//
//     /* unknown option */
//     if (argument[0] == '-') {
//       printf("Error: unknown option %s\n\n", argument);
//       printf("To get help on usage:\n");
//       printf("  6pack --help\n\n");
//       return -1;
//     }
//
//     /* first specified file is input */
//     if (!input_file) {
//       input_file = argument;
//       continue;
//     }
//
//     /* next specified file is output */
//     if (!output_file) {
//       output_file = argument;
//       continue;
//     }
//
//     /* files are already specified */
//     printf("Error: unknown option %s\n\n", argument);
//     printf("To get help on usage:\n");
//     printf("  6pack --help\n\n");
//     return -1;
//   }
//
//   if (!input_file) {
//     printf("Error: input file is not specified.\n\n");
//     printf("To get help on usage:\n");
//     printf("  6pack --help\n\n");
//     return -1;
//   }
//
//   if (!output_file && !benchmark) {
//     printf("Error: output file is not specified.\n\n");
//     printf("To get help on usage:\n");
//     printf("  6pack --help\n\n");
//     return -1;
//   }
//
// #ifdef SIXPACK_BENCHMARK_WIN32
//   if (benchmark)
//     return benchmark_speed(compress_level, input_file);
//   else
// #endif
//     return pack_file(compress_level, input_file, output_file);
//
//   /* unreachable */
//   return 0;
// }

        // See https://aka.ms/new-console-template for more information
        Console.WriteLine("Hello, World!");
        return 0;
    }
}