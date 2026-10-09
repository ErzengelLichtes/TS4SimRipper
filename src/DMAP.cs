/* Xmods Data Library, a library to support tools for The Sims 4,
   Copyright (C) 2014  C. Marinetti

   This program is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.

   You should have received a copy of the GNU General Public License
   along with this program.  If not, see <http://www.gnu.org/licenses/>. 
   The author may be contacted at modthesims.info, username cmarNYC.
   with thanks to EA/SimGuruModSquad for file description and code,
   and Kuree for initial translation into C# */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TS4SimRipper
{
    public class DMap {
        uint           version;       // current is 7
        uint           doubledWidth;
        uint           height;
        uint           ageGender;
        uint           species;
        byte           physique;
        ShapeOrNormals shapeOrNormals;
        uint           minCol;
        uint           maxCol;
        uint           minRow;
        uint           maxRow;
        RobeChannel    robeChannel;
        float          skinTightMinVal;
        float          skinTightDelta;
        float          robeMinVal;
        float          robeDelta;
        int            totalBytes;
        ScanLine[]     scanLines;

        public float     weight = 1f;
        public string    package;
        public ulong     instance;
        public SimRegion region = SimRegion.ALL;

        public uint Version => version;

        public uint Width => doubledWidth / 2;

        public uint Height => height;

        public float SkinTightMinVal => (version >= 7) ? skinTightMinVal : ((shapeOrNormals == 0) ? -0.2f : -0.75f);

        public float SkinTightDelta => (version >= 7) ? skinTightDelta : ((shapeOrNormals == 0) ? 0.4f : 1.5f);

        public float RobeMinVal => (version >= 7) ? robeMinVal : ((shapeOrNormals == 0) ? -0.2f : -0.75f);

        public float RobeDelta => (version >= 7) ? robeDelta : ((shapeOrNormals == 0) ? 0.4f : 1.5f);

        public Species Species => (Species)species;

        public AgeGender AgeGender => (AgeGender)ageGender;

        public Physiques Physique => (Physiques)physique;

        public ShapeOrNormals ShapeOrNormal => (ShapeOrNormals)shapeOrNormals;

        public bool RobeDataPresent => (robeChannel == 0);

        public uint MinCol => minCol;

        public uint MaxCol => maxCol;

        public uint MinRow => minRow;

        public uint MaxRow => maxRow;

        public bool UseRobeChannel => robeChannel != RobeChannel.ROBECHANNEL_DROPPED;

        public bool HasData => scanLines.Length > 0;

        public DMap(BinaryReader br) {
            br.BaseStream.Position = 0;
            version           = br.ReadUInt32();

            // For unsupported versions, capture raw bytes for debugging
            if (version > 9) {
                br.BaseStream.Position = 0;
                byte[] headerBytes = br.ReadBytes(Math.Min(256, (int)br.BaseStream.Length));
                string hexDump     = BitConverter.ToString(headerBytes).Replace("-", " ");
                Debug.WriteLine($"UNSUPPORTED DMap version {version} - First 256 bytes (hex): {hexDump}");

                // Parse a few key fields to show structure
                br.BaseStream.Position = 0;
                uint v   = br.ReadUInt32();
                uint dw  = br.ReadUInt32();
                uint h   = br.ReadUInt32();
                uint ag  = br.ReadUInt32();
                uint sp  = br.ReadUInt32();
                byte phy = br.ReadByte();
                byte sn  = br.ReadByte();
                Debug
                      .WriteLine($"V9 DEBUG: version={v}, doubledWidth={dw}, height={h}, ageGender={ag}, species={sp}, physique={phy}, shapeOrNormals={sn}, pos={br.BaseStream.Position}");

                // Read next 32 bytes
                byte[] nextBytes = br.ReadBytes(32);
                string nextHex   = BitConverter.ToString(nextBytes).Replace("-", " ");
                Debug.WriteLine($"V9 DEBUG: Next 32 bytes from offset 22: {nextHex}");
                Debug.WriteLine($"Stream length: {br.BaseStream.Length}");

                throw new NotSupportedException($"DMap format version {version} is not supported.");
            }

            doubledWidth = br.ReadUInt32();
            height       = br.ReadUInt32();
            ageGender    = br.ReadUInt32();
            // Version 6 and later always have species field
            if (version >= 6) species = br.ReadUInt32();
            physique       = br.ReadByte();
            shapeOrNormals = (ShapeOrNormals)br.ReadByte();
            if (version >= 9) {
                var unknownX = br.ReadByte();
                var unknownY = br.ReadByte();
                var unknownZ = br.ReadByte();
            }

            minCol      = br.ReadUInt32();
            maxCol      = br.ReadUInt32();
            minRow      = br.ReadUInt32();
            maxRow      = br.ReadUInt32();
            robeChannel = (RobeChannel)br.ReadByte();

            // Sanity check: validate that dimensions are reasonable
            if (minCol > maxCol
             || minRow > maxRow
             || maxCol > 10000
             || maxRow > 10000) {
                throw new InvalidOperationException($"Invalid DMap dimensions: MinCol={minCol}, MaxCol={maxCol}, MinRow={minRow}, MaxRow={maxRow}");
            }

            // Debug: Log the header we just read
            Debug.WriteLine($"DMap Header - Version:{version}, Width:{doubledWidth}, Height:{height}, Species:{species}, MinCol:{minCol}, MaxCol:{maxCol}, MinRow:{minRow}, MaxRow:{maxRow}, RobeChannel:{robeChannel}, StreamPos:{br.BaseStream.Position}/{br.BaseStream.Length}");

            if (version > 6) {
                skinTightMinVal = br.ReadSingle();
                skinTightDelta  = br.ReadSingle();
                if (robeChannel == RobeChannel.ROBECHANNEL_PRESENT) {
                    robeMinVal = br.ReadSingle();
                    robeDelta  = br.ReadSingle();
                }
                else {
                    robeMinVal = skinTightMinVal;
                    robeDelta  = skinTightDelta;
                }
            }

            totalBytes = br.ReadInt32();

            if (version >= 9) {
                long start = br.BaseStream.Position;   // just after totalBytes
                int  lines = 0;
                var  sizes = new List<int>();
                while (br.BaseStream.Position + 2 <= br.BaseStream.Length) {
                    long   linePos = br.BaseStream.Position;
                    ushort len     = br.ReadUInt16();
                    if (len < 2) {
                        Debug.WriteLine($"Bad length {len} at {linePos}");
                        break;
                    }

                    br.BaseStream.Position = linePos + len;
                    sizes.Add(len);
                    lines++;
                }

                Debug.WriteLine($"Walked {lines} lines, ended at {br.BaseStream.Position}/{br.BaseStream.Length}");
                Debug.WriteLine($"First 30 sizes: {string.Join(",", sizes.Take(30))}");
                br.BaseStream.Seek(start, SeekOrigin.Begin);
            }

            // Sanity check: totalBytes should not exceed remaining stream size
            long remainingBytes = br.BaseStream.Length - br.BaseStream.Position;
            if (totalBytes > remainingBytes
             || totalBytes < 0) {
                throw new InvalidOperationException($"Invalid totalBytes value {totalBytes}: exceeds remaining stream ({remainingBytes} bytes)");
            }

            Debug
                  .WriteLine($"DMap Read totalBytes:{totalBytes}, StreamPos:{br.BaseStream.Position}/{br.BaseStream.Length}, Will create {maxRow - minRow + 1} scanlines");

            if (totalBytes == 0) {
                scanLines = Array.Empty<ScanLine>();
            }
            else {
                int  width        = (int)(maxCol - minCol + 1);
                uint numScanLines = maxRow - minRow + 1;
                scanLines = new ScanLine[numScanLines];
                for (int i = 0; i < numScanLines; i++) {
                    scanLines[i] = new ScanLine(width, br, version);
                }
            }
        }

        /// <summary>
        /// Make a DMap from an array of delta vectors: skintightDeltas[w, h], robeDeltas[w, h] (robeDeltas may be null)
        /// </summary>
        /// <param name="ageGender">AgeGender</param>
        /// <param name="physique">Physique</param>
        /// <param name="shapeNormals">Shape or Normals</param>
        /// <param name="minColumn">first column containing data</param>
        /// <param name="maxColumn">last column containing data</param>
        /// <param name="minRow">first row containing data</param>
        /// <param name="maxRow">last row containing data</param>
        /// <param name="skinDeltas">Array of Delta Vectors, [row][column]</param>
        /// <param name="robeDeltas">Array of Delta Vectors, [row][column], may be null if there is no robe data</param>
        public DMap(Species        species,
                    AgeGender      ageGender,
                    Physiques      physique,
                    RobeChannel    hasRobeData,
                    ShapeOrNormals shapeNormals,
                    uint           minColumn,
                    uint           maxColumn,
                    uint           minRow,
                    uint           maxRow,
                    Vector3[][]    skinDeltas,
                    Vector3[][]    robeDeltas,
                    bool           verticalFlip
        ) {
            version        = 7;
            height         = (uint)skinDeltas.Length;
            doubledWidth   = (uint)(skinDeltas[0].Length * 2);
            this.ageGender      = (uint)ageGender;
            this.species        = (uint)species;
            this.physique       = (byte)physique;
            shapeOrNormals = shapeNormals;
            minCol         = minColumn;
            maxCol         = maxColumn;
            this.minRow         = minRow;
            this.maxRow         = maxRow;
            robeChannel    = hasRobeData;

            float minVal = float.MaxValue;
            float maxVal = float.MinValue;
            for (uint h = minRow; h <= maxRow; h++) {
                for (uint w = minCol; w <= maxCol; w++) {
                    Vector3 v                        = skinDeltas[h][w];
                    if (v.X         < minVal) minVal = v.X;
                    if (v.Y         < minVal) minVal = v.Y;
                    if (v.Z         < minVal) minVal = v.Z;
                    if (v.X         > maxVal) maxVal = v.X;
                    if (v.Y         > maxVal) maxVal = v.Y;
                    if (v.Z         > maxVal) maxVal = v.Z;
                    if (hasRobeData != RobeChannel.ROBECHANNEL_PRESENT) continue;
                    v = robeDeltas[h][w];
                    if (v.X < minVal) minVal = v.X;
                    if (v.Y < minVal) minVal = v.Y;
                    if (v.Z < minVal) minVal = v.Z;
                    if (v.X > maxVal) maxVal = v.X;
                    if (v.Y > maxVal) maxVal = v.Y;
                    if (v.Z > maxVal) maxVal = v.Z;
                }
            }

            float tmp = Math.Max(Math.Abs(minVal), Math.Abs(maxVal)) + 0.05f;
            //  minVal = Math.Min((ShapeOrNormal == ShapeOrNormals.SHAPE_DEFORMER ? -0.2f : -.75f), -tmp);
            minVal = -tmp;
            float deltaVal = Math.Abs(minVal) * 2.0f;

            //minVal = -0.75f;
            //deltaVal = 1.50f;

            if (minCol >= maxCol) {
                skinTightMinVal = -0.75f;
                skinTightDelta  = 1.5f;
            }
            else {
                skinTightMinVal = minVal;
                skinTightDelta  = deltaVal;
            }

            if (hasRobeData == RobeChannel.ROBECHANNEL_PRESENT) {
                robeMinVal = skinTightMinVal;
                robeDelta  = skinTightDelta;
            }

            int            width    = (int)(maxCol - minCol + 1);
            List<ScanLine> scanners = new List<ScanLine>();
            byte           zero     = (byte)0x80;
            Vector3        vZero    = new Vector3();
            for (uint r = minRow; r < maxRow + 1; r++) {
                RobeChannel robeDataLine = hasRobeData;
                if (hasRobeData == RobeChannel.ROBECHANNEL_PRESENT) {
                    Vector3[] skinRow = skinDeltas[r];
                    Vector3[] robeRow = robeDeltas[r];
                    if (skinRow.SequenceEqual(robeRow)) {
                        robeDataLine = RobeChannel.ROBECHANNEL_ISCOPY;
                        bool drop = robeRow.All(v => v == vZero);

                        if (drop) robeDataLine = RobeChannel.ROBECHANNEL_DROPPED;
                    }
                }

                var pixels = new List<byte>();
                for (uint c = minCol; c < maxCol + 1 ; c++) {
                    Vector3 v = skinDeltas[r][c];
                    pixels.Add(Math.Abs(v.X) > .0001f ? (byte)((((double)v.X + minVal) * 255.0d) / deltaVal) : zero);
                    pixels.Add(Math.Abs(v.Y) > .0001f ? (byte)((((double)v.Y + minVal) * 255.0d) / deltaVal) : zero);
                    pixels.Add(Math.Abs(v.Z) > .0001f ? (byte)((((double)v.Z + minVal) * 255.0d) / deltaVal) : zero);
                    if (robeDataLine != RobeChannel.ROBECHANNEL_PRESENT) continue;
                    Vector3 vr = robeDeltas[r][c];
                    pixels.Add(Math.Abs(vr.X) > .0001f ? (byte)((((double)vr.X + minVal) * 255.0d) / deltaVal) : zero);
                    pixels.Add(Math.Abs(vr.Y) > .0001f ? (byte)((((double)vr.Y + minVal) * 255.0d) / deltaVal) : zero);
                    pixels.Add(Math.Abs(vr.Z) > .0001f ? (byte)((((double)vr.Z + minVal) * 255.0d) / deltaVal) : zero);
                }

                scanners.Add(new ScanLine(width, robeDataLine, pixels.ToArray()));
            }

            if (verticalFlip) {
                scanners.Reverse();
                uint h = this.maxRow - this.minRow;
                this.minRow = height      - this.maxRow - 1;
                this.maxRow = this.minRow + h;
            }

            scanLines = scanners.ToArray();
        }

        public void Write(BinaryWriter bw) {
            bw.Write(version);
            bw.Write(doubledWidth);
            bw.Write(height);
            bw.Write(ageGender);
            if (version > 5) bw.Write(species);
            bw.Write(physique);
            bw.Write((byte)shapeOrNormals);
            bw.Write(minCol);
            bw.Write(maxCol);
            bw.Write(minRow);
            bw.Write(maxRow);
            bw.Write((byte)robeChannel);
            if (version > 6) {
                bw.Write(skinTightMinVal);
                bw.Write(skinTightDelta);
                if (robeChannel == RobeChannel.ROBECHANNEL_PRESENT) {
                    bw.Write(robeMinVal);
                    bw.Write(robeDelta);
                }
            }

            scanLines ??= Array.Empty<ScanLine>();
            uint totalBytes = scanLines.Aggregate<ScanLine, uint>(0, (current, t) => current + t.scanLineDataSize);

            bw.Write(totalBytes);
            foreach(ScanLine line in scanLines) {
                line.Write(bw);
            }
        }

        //public void Smooth()
        //{
        //    for (int i = 0; i < scanLines.Length; i++)
        //    {
        //        int pixelLength = scanLines[i].robeChannel == RobeChannel.ROBECHANNEL_PRESENT ? 6 : 3;
        //        byte[] tmp = new byte[scanLines[i].uncompressedPixels.Length];
        //        for (int j = 2 * pixelLength; j <= scanLines[i].uncompressedPixels.Length - (3 * pixelLength); j += pixelLength)
        //        {
        //            int tot1 = 0; int tot2 = 0; int tot3 = 0;
        //            int totr1 = 0; int totr2 = 0; int totr3 = 0;
        //            for (int k = j - (2 * pixelLength); k < j + (3 * pixelLength); k += pixelLength)
        //            {
        //                tot1 += scanLines[i].uncompressedPixels[k];
        //                tot2 += scanLines[i].uncompressedPixels[k + 1];
        //                tot3 += scanLines[i].uncompressedPixels[k + 2];
        //                if (pixelLength == 6)
        //                {
        //                    totr1 += scanLines[i].uncompressedPixels[k + 3];
        //                    totr2 += scanLines[i].uncompressedPixels[k + 4];
        //                    totr3 += scanLines[i].uncompressedPixels[k + 5];
        //                }
        //            }
        //            tmp[j] = (byte)((tot1 / 5f) + .5);
        //            tmp[j + 1] = (byte)((tot2 / 5) + .5);
        //            tmp[j + 2] = (byte)((tot3 / 5) + .5);
        //            if (pixelLength == 6)
        //            {
        //                tmp[j + 3] = (byte)((totr1 / 5f) + .5);
        //                tmp[j + 4] = (byte)((totr2 / 5) + .5);
        //                tmp[j + 5] = (byte)((totr3 / 5) + .5);
        //            }
        //        }
        //        Array.Copy(tmp, 2 * pixelLength, scanLines[i].uncompressedPixels, 2 * pixelLength, scanLines[i].uncompressedPixels.Length - (2 * pixelLength));
        //    }
        //}

        public class ScanLine {
            internal UInt16          scanLineDataSize;
            internal CompressionType isCompressed;
            internal RobeChannel     robeChannel;
            internal byte[]          uncompressedPixels;
            internal byte            numIndexes;
            internal UInt16[]        pixelPosIndexes;
            internal UInt16[]        dataPosIndexes;
            internal byte[]          RLEArrayOfPixels;
            int                      width;

            public ScanLine(int width, BinaryReader br, uint version) {
                long lineStart = br.BaseStream.Position;

                this.width            = width;
                scanLineDataSize = br.ReadUInt16();
                isCompressed     = (CompressionType)br.ReadByte();
                if (isCompressed == CompressionType.NoData) {
                    robeChannel = RobeChannel.ROBECHANNEL_DROPPED;
                }
                else {
                    robeChannel = (RobeChannel)br.ReadByte();
                }

                switch(isCompressed) {
                case CompressionType.None: {
                    if (version >= 9) {
                        // Size prefix includes its own 2 bytes + compression byte + robe byte
                        uncompressedPixels = br.ReadBytes(scanLineDataSize - 4);
                    }
                    else if (robeChannel == RobeChannel.ROBECHANNEL_PRESENT) {
                        uncompressedPixels = br.ReadBytes(width * 6);
                    }
                    else {
                        uncompressedPixels = br.ReadBytes(width * 3);
                    }

                    break;
                }
                case CompressionType.RLE: {
                    numIndexes      = br.ReadByte();
                    pixelPosIndexes = new UInt16[numIndexes];
                    dataPosIndexes  = new UInt16[numIndexes];
                    for (int i = 0; i < numIndexes; i++)
                        pixelPosIndexes[i] = br.ReadUInt16();
                    for (int i = 0; i < numIndexes; i++)
                        dataPosIndexes[i] = br.ReadUInt16();
                    uint headerdatasize = 4U + 1U + (4U * numIndexes);
                    RLEArrayOfPixels = br.ReadBytes((int)(scanLineDataSize - headerdatasize));
                    break;
                }
                }

                // V9 diagnostic and resync: the size prefix includes its own 2 bytes,
                // which your length walk confirmed lands exactly on the end of the stream.
                if (version >= 9) {
                    long expectedEnd = lineStart + scanLineDataSize;
                    if (br.BaseStream.Position == expectedEnd) return;
                    Debug.WriteLine($"ScanLine size mismatch: compression={isCompressed}, robe={robeChannel}, " + $"read to {br.BaseStream.Position}, expected {expectedEnd}");
                    br.BaseStream.Position = expectedEnd;
                }
            }

            public ScanLine(ScanLine other) {
                width            = other.width;
                scanLineDataSize = other.scanLineDataSize;
                isCompressed     = other.isCompressed;
                robeChannel      = (RobeChannel)(byte)other.robeChannel;

                switch(isCompressed) {
                case CompressionType.None:
                    uncompressedPixels = new byte[other.uncompressedPixels.Length];
                    Array.Copy(other.uncompressedPixels, uncompressedPixels, other.uncompressedPixels.Length);
                    break;
                case CompressionType.RLE: {
                    numIndexes      = other.numIndexes;
                    pixelPosIndexes = new UInt16[numIndexes];
                    dataPosIndexes  = new UInt16[numIndexes];
                    Array.Copy(other.pixelPosIndexes, pixelPosIndexes, numIndexes);
                    Array.Copy(other.dataPosIndexes,  dataPosIndexes,  numIndexes);
                    uint headerdatasize = 4U + 1U + (4U * numIndexes);
                    RLEArrayOfPixels = new byte[other.RLEArrayOfPixels.Length];
                    Array.Copy(other.RLEArrayOfPixels, RLEArrayOfPixels, other.RLEArrayOfPixels.Length);
                    break;
                }
                }
            }

            public ScanLine(int width, RobeChannel hasRobeData, byte[] pixels) {
                this.width            = width;
                scanLineDataSize = (ushort)(pixels.Length + 4);
                isCompressed     = CompressionType.None;
                robeChannel      = hasRobeData;

                if (isCompressed == CompressionType.None) {
                    uncompressedPixels = new byte[pixels.Length];
                    Array.Copy(pixels, uncompressedPixels, pixels.Length);
                }
                //else
                //{
                //    this.numIndexes = br.ReadByte();
                //    this.pixelPosIndexes = new UInt16[numIndexes];
                //    this.dataPosIndexes = new UInt16[numIndexes];
                //    for (int i = 0; i < numIndexes; i++) this.pixelPosIndexes[i] = br.ReadUInt16();
                //    for (int i = 0; i < numIndexes; i++) this.dataPosIndexes[i] = br.ReadUInt16();
                //    uint headerdatasize = 4U + 1U + (4U * numIndexes);
                //    this.RLEArrayOfPixels = br.ReadBytes((int)(scanLineDataSize - headerdatasize));
                //}

            }

            public void Write(BinaryWriter bw) {
                bw.Write(scanLineDataSize);
                bw.Write((byte)isCompressed);
                if (isCompressed != CompressionType.NoData) bw.Write((byte)robeChannel);

                switch(isCompressed) {
                case CompressionType.None:
                    bw.Write(uncompressedPixels);
                    break;
                case CompressionType.RLE: {
                    bw.Write(numIndexes);
                    for (int i = 0; i < numIndexes; i++) bw.Write(pixelPosIndexes[i]);
                    for (int i = 0; i < numIndexes; i++) bw.Write(dataPosIndexes[i]);
                    bw.Write(RLEArrayOfPixels);
                    break;
                }
                }
            }
        }

        public enum OutputType {
            Skin,
            Robe
        }

        public enum CompressionType : byte {
            None   = 0,
            RLE    = 1,
            NoData = 2
        }

        public Stream ToBitMap(OutputType type) {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w  = new BinaryWriter(ms);
            if (maxCol == 0) return null;
            int height = (int)(maxRow      - minRow      + 1);
            int width  = (int)(maxCol - minCol + 1);

            byte[] pixelArraySkinTight = new byte[width * height * 3];
            byte[] pixelArrayRobe      = new byte[width * height * 3];

            int destIndexRobe = 0;
            int destSkinTight = 0;

            int pixelsize = 0;

            for (int i = 0; i < height; i++) {
                pixelsize = scanLines[i].robeChannel == RobeChannel.ROBECHANNEL_PRESENT ? 6 : 3;

                var scan = scanLines[i];
                switch(scan.isCompressed) {
                case CompressionType.None: {
                    for (int j = 0; j < width; j++) {
                        pixelArraySkinTight[destSkinTight++] = scan.uncompressedPixels[(j * pixelsize) + 0];
                        pixelArraySkinTight[destSkinTight++] = scan.uncompressedPixels[(j * pixelsize) + 1];
                        pixelArraySkinTight[destSkinTight++] = scan.uncompressedPixels[(j * pixelsize) + 2];

                        switch (scan.robeChannel) {
                        case RobeChannel.ROBECHANNEL_PRESENT:
                            pixelArrayRobe[destIndexRobe++] = scan.uncompressedPixels[(j * pixelsize) + 3];
                            pixelArrayRobe[destIndexRobe++] = scan.uncompressedPixels[(j * pixelsize) + 4];
                            pixelArrayRobe[destIndexRobe++] = scan.uncompressedPixels[(j * pixelsize) + 5];
                            break;
                        case RobeChannel.ROBECHANNEL_DROPPED:
                            pixelArrayRobe[destIndexRobe++] = 0x80;
                            pixelArrayRobe[destIndexRobe++] = 0x80;
                            pixelArrayRobe[destIndexRobe++] = 0x80;
                            break;
                        case RobeChannel.ROBECHANNEL_ISCOPY:
                            pixelArrayRobe[destIndexRobe++] = scan.uncompressedPixels[(j * pixelsize) + 0];
                            pixelArrayRobe[destIndexRobe++] = scan.uncompressedPixels[(j * pixelsize) + 1];
                            pixelArrayRobe[destIndexRobe++] = scan.uncompressedPixels[(j * pixelsize) + 2];
                            break;
                        }
                    }

                    break;
                }
                case CompressionType.RLE: {
                    // Look up each pixel using index tables
                    for (int j = 0; j < width; j++) {
                        // To get pointer to the RLE encoded data we need first find 
                        // proper RLE run in the buffer. Use index for this:

                        // Cache increment for indexing in pixel space?
                        int step = 1 + width / (scan.numIndexes - 1); // 1 entry was added for the remainder of the division

                        // Find index into the positions and data table:
                        int idx = j / step;

                        // This is location of the run first covering this interval.
                        int pixelPosX = scan.pixelPosIndexes[idx];

                        // Position of the RLE data of the place where need to unwind to the pixel. 
                        int dataPos = scan.dataPosIndexes[idx] * (pixelsize + 1); // +1 for run length byte

                        // This is run length for the RLE entry found at 
                        int runLength = scan.RLEArrayOfPixels[dataPos];

                        // Loop forward unwinding RLE data from the found indexed position. 
                        // Continue until the pixel position in question is not covered 
                        // by the current run interval. By design the loop should execute 
                        // only few times until we find the value we are looking for.
                        while (j >= pixelPosX + runLength) {
                            pixelPosX += runLength;
                            dataPos   += (1 + pixelsize); // 1 for run length, +pixelSize for the run value

                            runLength = scan.RLEArrayOfPixels[dataPos];
                        }

                        // After breaking out of the cycle, we have the current run length interval
                        // covering the pixel position x we are interested in. So just return the pointer
                        // to the pixel data we were after:
                        int pixelStart = dataPos + 1;

                        //
                        pixelArraySkinTight[destSkinTight++] = scan.RLEArrayOfPixels[pixelStart + 0];
                        pixelArraySkinTight[destSkinTight++] = scan.RLEArrayOfPixels[pixelStart + 1];
                        pixelArraySkinTight[destSkinTight++] = scan.RLEArrayOfPixels[pixelStart + 2];
                        switch (scan.robeChannel) {
                        case RobeChannel.ROBECHANNEL_PRESENT:
                            pixelArrayRobe[destIndexRobe++] = scan.RLEArrayOfPixels[pixelStart + 3];
                            pixelArrayRobe[destIndexRobe++] = scan.RLEArrayOfPixels[pixelStart + 4];
                            pixelArrayRobe[destIndexRobe++] = scan.RLEArrayOfPixels[pixelStart + 5];
                            break;
                        case RobeChannel.ROBECHANNEL_DROPPED:
                            pixelArrayRobe[destIndexRobe++] = 0x80;
                            pixelArrayRobe[destIndexRobe++] = 0x80;
                            pixelArrayRobe[destIndexRobe++] = 0x80;
                            break;
                        case RobeChannel.ROBECHANNEL_ISCOPY:
                            pixelArrayRobe[destIndexRobe++] = scan.RLEArrayOfPixels[pixelStart + 0];
                            pixelArrayRobe[destIndexRobe++] = scan.RLEArrayOfPixels[pixelStart + 1];
                            pixelArrayRobe[destIndexRobe++] = scan.RLEArrayOfPixels[pixelStart + 2];
                            break;
                        }
                    }

                    break;
                }
                case CompressionType.NoData: {
                    for (int j = 0; j < width; j++) {
                        pixelArraySkinTight[destSkinTight++] = 0x80;
                        pixelArraySkinTight[destSkinTight++] = 0x80;
                        pixelArraySkinTight[destSkinTight++] = 0x80;
                        pixelArrayRobe[destIndexRobe++]      = 0x80;
                        pixelArrayRobe[destIndexRobe++]      = 0x80;
                        pixelArrayRobe[destIndexRobe++]      = 0x80;
                    }

                    break;
                }
                }

            }

            w.Write((ushort)0x4d42);
            w.Write(0);
            w.Write(0);
            w.Write(54);
            w.Write(40);
            w.Write(width);
            w.Write(height);
            w.Write((ushort)1);
            w.Write((ushort)24);
            for (int i = 0; i < 6; i++) w.Write(0);

            int bytesPerLine          = (int)Math.Ceiling(width * 24.0 / 8.0);
            int padding               = 4 - bytesPerLine % 4;
            if (padding == 4) padding = 0;
            long sourcePosition       = 0;

            for (int i = 0; i < height; i++) {
                for (int j = 0; j < width * 3; j++) {
                    w.Write(type == OutputType.Robe ? pixelArrayRobe[sourcePosition++] : pixelArraySkinTight[sourcePosition++]);
                }

                for (int j = 0; j < padding; j++) {
                    w.Write((byte)0);
                }
            }

            return ms;
        }

        public void Uncompress() {
            if (maxCol == 0) return;
            int height = (int)(maxRow      - minRow      + 1);
            int width  = (int)(maxCol - minCol + 1);

            int pixelsize = 0;

            for (int i = 0; i < height; i++) {
                pixelsize = scanLines[i].robeChannel == RobeChannel.ROBECHANNEL_PRESENT ? 6 : 3;

                byte[] pixelArray = new byte[width * pixelsize];
                int    dest       = 0;

                var scan = scanLines[i];
                switch(scan.isCompressed) {
                case CompressionType.RLE: {
                    for (int j = 0; j < width; j++) {
                        int step      = 1 + width / (scan.numIndexes - 1); // 1 entry was added for the remainder of the division
                        int idx       = j / step;
                        int pixelPosX = scan.pixelPosIndexes[idx];
                        int dataPos   = scan.dataPosIndexes[idx] * (pixelsize + 1); // +1 for run length byte
                        int runLength = scan.RLEArrayOfPixels[dataPos];
                        while (j >= pixelPosX + runLength) {
                            pixelPosX += runLength;
                            dataPos   += (1 + pixelsize); // 1 for run length, +pixelSize for the run value
                            runLength =  scan.RLEArrayOfPixels[dataPos];
                        }

                        int pixelStart = dataPos + 1;
                        pixelArray[dest++] = scan.RLEArrayOfPixels[pixelStart + 0];
                        pixelArray[dest++] = scan.RLEArrayOfPixels[pixelStart + 1];
                        pixelArray[dest++] = scan.RLEArrayOfPixels[pixelStart + 2];
                        if (scan.robeChannel == RobeChannel.ROBECHANNEL_PRESENT) {
                            pixelArray[dest++] = scan.RLEArrayOfPixels[pixelStart + 3];
                            pixelArray[dest++] = scan.RLEArrayOfPixels[pixelStart + 4];
                            pixelArray[dest++] = scan.RLEArrayOfPixels[pixelStart + 5];
                        }
                    }

                    scan.uncompressedPixels = pixelArray;
                    scan.isCompressed       = 0;
                    scan.scanLineDataSize   = (ushort)(pixelArray.Length + 4);
                    break;
                }
                case CompressionType.NoData: {
                    for (int j = 0; j < width; j++) {
                        pixelArray[dest++]      = 0x80;
                        pixelArray[dest++]      = 0x80;
                        pixelArray[dest++]      = 0x80;
                        scan.uncompressedPixels = pixelArray;
                        scan.isCompressed       = 0;
                        scan.robeChannel        = RobeChannel.ROBECHANNEL_DROPPED;
                        scan.scanLineDataSize   = 3;
                    }

                    break;
                }
                }
            }
        }

        public void Compress() {
            if (maxCol == 0) return;
            int height = (int)(maxRow      - minRow      + 1);
            int width  = (int)(maxCol - minCol + 1);

            int pixelsize = 0;

            for (int i = 0; i < height; i++) {
                pixelsize = scanLines[i].robeChannel == RobeChannel.ROBECHANNEL_PRESENT ? 6 : 3;

                if (scanLines[i].isCompressed != CompressionType.None) continue;
                var  scan   = new ScanLine(scanLines[i]);
                bool noData = scan.uncompressedPixels.All(b => b == 0x80);

                if (noData) {
                    scan.scanLineDataSize = 3;
                    scan.isCompressed     = CompressionType.NoData;
                    scan.robeChannel      = RobeChannel.ROBECHANNEL_DROPPED;
                }
                else {
                    List<byte>   compressed = new List<byte>();
                    List<ushort> pixelInd   = new List<ushort>();
                    List<ushort> dataInd    = new List<ushort>();
                    byte[]       currentVal = new byte[pixelsize];
                    for (int p = 0; p < pixelsize; p++)             //copy first pixel set to start
                    {
                        currentVal[p] = scan.uncompressedPixels[p];
                    }

                    int pixInd  = 0;
                    int runSize = 0;
                    for (int j = 0; j < width; j++) {
                        byte[] testVal = new byte[pixelsize];
                        for (int p = 0; p < pixelsize; p++)    //copy next pixel set
                        {
                            testVal[p] = scan.uncompressedPixels[(j * pixelsize) + p];
                        }

                        if (currentVal.SequenceEqual(testVal)
                         && runSize < 255)  //test for different value
                        {
                            runSize++;
                        }
                        else                                //if run of same values is over, write the count and values
                        {
                            pixelInd.Add((ushort)pixInd);
                            dataInd.Add((ushort)(compressed.Count / (pixelsize + 1)));
                            compressed.Add((byte)runSize);
                            for (int p = 0; p < pixelsize; p++) {
                                compressed.Add(currentVal[p]);
                            }

                            pixInd  = j;
                            runSize = 1;
                            Array.Copy(testVal, currentVal, pixelsize);
                        }
                    }

                    pixelInd.Add((ushort)pixInd);                   //add last set
                    dataInd.Add((ushort)(compressed.Count / (pixelsize + 1)));
                    compressed.Add((byte)runSize);
                    for (int p = 0; p < pixelsize; p++) {
                        compressed.Add(currentVal[p]);
                    }

                    pixelInd.Add(pixelInd[^1]);     //duplicate last index
                    dataInd.Add(dataInd[^1]);

                    int step = width / pixelInd.Count;
                    for (int s = 1; s < pixelInd.Count; s++) {
                        if (pixelInd[s] <= s * step) continue;
                        pixelInd[s] = pixelInd[s - 1];
                        dataInd[s]  = dataInd[s  - 1];
                    }

                    scan.numIndexes       = (byte)pixelInd.Count;
                    scan.pixelPosIndexes  = pixelInd.ToArray();
                    scan.dataPosIndexes   = dataInd.ToArray();
                    scan.RLEArrayOfPixels = compressed.ToArray();
                    scan.isCompressed     = CompressionType.RLE;
                    scan.scanLineDataSize = (ushort)((scan.numIndexes * 4) + scan.RLEArrayOfPixels.Length + 5);
                }

                if (noData || (scan.pixelPosIndexes.Length <= byte.MaxValue && scan.scanLineDataSize < scanLines[i].uncompressedPixels.Length + 5)) scanLines[i] = scan;
            }
        }

        public MorphMap ToMorphMap() {
            if (maxCol == 0)
                return null;
            int height = (int)(maxRow - minRow + 1);
            int width = (int)(maxCol - minCol + 1);

            // Values are stored normalized (raw / max, 0..1), or NaN meaning "no delta" (the sentinel).
            // This lets lines with different bit depths (8-bit or 11/11/10 packed) share one pipeline.
            double[] skinNorm = new double[width * height * 3];
            double[] robeNorm = new double[width * height * 3];
            bool[] robeDataRow = new bool[height];

            int destSkin = 0;
            int destRobe = 0;

            for (int i = 0; i < height; i++) {
                var scan = scanLines[i];
                bool robePresent = scan.robeChannel == RobeChannel.ROBECHANNEL_PRESENT;
                robeDataRow[i] = robePresent;

                switch(scan.isCompressed) {
                case CompressionType.None: {
                    // Bytes per pixel comes from the data itself, not from assumptions about the version
                    int pixelBytes = scan.uncompressedPixels.Length / width;
                    int setBytes   = GetSetBytes(pixelBytes, robePresent, i, scan.isCompressed, scan.uncompressedPixels.Length, width);

                    for (int j = 0; j < width; j++) {
                        int p = j * pixelBytes;

                        ReadPixel(scan.uncompressedPixels, p, setBytes, skinNorm, destSkin);
                        destSkin += 3;

                        WriteRobe(scan.robeChannel, scan.uncompressedPixels, p, setBytes, robeNorm, destRobe);
                        destRobe += 3;
                    }

                    break;
                }
                case CompressionType.RLE: {
                    int entrySize = DetectRleEntrySize(scan.RLEArrayOfPixels, width, robePresent);
                    if (entrySize < 0) {
                        throw new InvalidDataException(
                                                       $"DMap line {i}: RLE data ({scan.RLEArrayOfPixels.Length} bytes) matches no known entry size for width {width}, robe={scan.robeChannel}");
                    }
                    int pixelBytes = entrySize - 1; // minus the run-length byte
                    int setBytes   = GetSetBytes(pixelBytes, robePresent, i, scan.isCompressed, scan.RLEArrayOfPixels.Length, width);

                    int step = 1 + width / (scan.numIndexes - 1); // 1 entry was added for the remainder of the division

                    for (int j = 0; j < width; j++) {
                        int idx       = j / step;
                        int pixelPosX = scan.pixelPosIndexes[idx];
                        int dataPos   = scan.dataPosIndexes[idx] * entrySize;
                        int runLength = scan.RLEArrayOfPixels[dataPos];
                        while (j >= pixelPosX + runLength) {
                            pixelPosX += runLength;
                            dataPos   += entrySize;

                            runLength = scan.RLEArrayOfPixels[dataPos];
                        }
                        int pixelStart = dataPos + 1;

                        ReadPixel(scan.RLEArrayOfPixels, pixelStart, setBytes, skinNorm, destSkin);
                        destSkin += 3;

                        WriteRobe(scan.robeChannel, scan.RLEArrayOfPixels, pixelStart, setBytes, robeNorm, destRobe);
                        destRobe += 3;
                    }

                    break;
                }
                case CompressionType.NoData: {
                    for (int j = 0; j < width; j++) {
                        for (int c = 0; c < 3; c++) {
                            skinNorm[destSkin++] = double.NaN;
                            robeNorm[destRobe++] = double.NaN;
                        }
                    }

                    break;
                }
                }
            }

            float Decode(double t, float min, float delta) =>
                double.IsNaN(t) ? 0f : (float)((t * delta) + min);

            long src = 0;
            Vector3[][] deltaSkin = new Vector3[height][];
            Vector3[][] deltaRobe = new Vector3[height][];
            for (int i = 0; i < height; i++) {
                deltaSkin[i] = new Vector3[width];
                deltaRobe[i] = new Vector3[width];
                for (int j = 0; j < width; j++) {
                    deltaSkin[i][j] = new Vector3(
                        Decode(skinNorm[src], SkinTightMinVal, SkinTightDelta),
                        Decode(skinNorm[src + 1], SkinTightMinVal, SkinTightDelta),
                        Decode(skinNorm[src + 2], SkinTightMinVal, SkinTightDelta));
                    deltaRobe[i][j] = new Vector3(
                        Decode(robeNorm[src], RobeMinVal, RobeDelta),
                        Decode(robeNorm[src + 1], RobeMinVal, RobeDelta),
                        Decode(robeNorm[src + 2], RobeMinVal, RobeDelta));
                    src += 3;
                }
            }

            return new MorphMap(Width, this.height, shapeOrNormals, minCol, maxCol,
                                minRow, maxRow, deltaSkin, deltaRobe, robeDataRow, weight, AgeGender,
                                package, instance, region);
        }

        /// <summary>
        /// Works out how many bytes one channel set (x,y,z) takes: 3 = 8-bit, 4 = 11/11/10 packed.
        /// With a robe the pixel holds two sets, so it is half the pixel size.
        /// </summary>
        private static int GetSetBytes(int pixelBytes, bool robePresent, int line, CompressionType type, int dataLength, int width) {
            int setBytes = robePresent ? pixelBytes / 2 : pixelBytes;
            bool ok = (!robePresent || pixelBytes % 2 == 0) && (setBytes == 3 || setBytes == 4);
            if (!ok) {
                throw new InvalidDataException(
                    $"DMap line {line}: unrecognized pixel layout ({type}, {pixelBytes} bytes/pixel, " +
                    $"data length {dataLength}, width {width}, robePresent={robePresent})");
            }
            return setBytes;
        }

        /// <summary>
        /// Finds the RLE entry size (1 run-length byte + pixel bytes) by walking the array:
        /// the right size makes the run lengths add up to exactly the width and end exactly at the array end.
        /// Returns -1 if none fit.
        /// </summary>
        private static int DetectRleEntrySize(byte[] data, int width, bool robePresent) {
            // pixel bytes: 3 or 4 without robe; 6 or 8 with robe
            int[] candidates = robePresent ? new[] { 9, 7 } : new[] { 5, 4 };
            foreach (int size in candidates) {
                if (data.Length % size != 0)
                    continue;
                int total = 0;
                for (int pos = 0; pos < data.Length; pos += size)
                    total += data[pos];
                if (total == width)
                    return size;
            }
            return -1;
        }

        /// <summary>
        /// Reads one channel set (3 components) into dest as normalized values; the "no delta" midpoint becomes NaN.
        /// 3 bytes: three 8-bit values (midpoint 0x80). 4 bytes: little-endian uint packed 11/11/10 bits
        /// (midpoints 1024/1024/512). The packed bit order is inferred, so verify it visually.
        /// </summary>
        private static void ReadPixel(byte[] data, int pos, int setBytes, double[] dest, int destPos) {
            if (setBytes == 4) {
                uint v = BitConverter.ToUInt32(data, pos);
                Store(dest, destPos, (int)(v & 0x7FF), 2047, 1024);          // bits 0-10
                Store(dest, destPos + 1, (int)((v >> 11) & 0x7FF), 2047, 1024); // bits 11-21
                Store(dest, destPos + 2, (int)(v >> 22), 1023, 512);          // bits 22-31
            }
            else {
                Store(dest, destPos, data[pos], 255, 0x80);
                Store(dest, destPos + 1, data[pos + 1], 255, 0x80);
                Store(dest, destPos + 2, data[pos + 2], 255, 0x80);
            }
        }

        private static void Store(double[] dest, int index, int raw, int max, int mid) {
            dest[index] = raw == mid ? double.NaN : raw / (double)max;
        }

        /// <summary>
        /// Fills the robe pixel according to the scanline's robe mode.
        /// pixelStart points at the skin set; the robe set (if present) follows it.
        /// </summary>
        private static void WriteRobe(RobeChannel mode, byte[] data, int pixelStart, int setBytes, double[] dest, int destPos) {
            switch (mode) {
                case RobeChannel.ROBECHANNEL_PRESENT:
                    ReadPixel(data, pixelStart + setBytes, setBytes, dest, destPos);
                    break;
                case RobeChannel.ROBECHANNEL_DROPPED:
                    dest[destPos] = double.NaN;
                    dest[destPos + 1] = double.NaN;
                    dest[destPos + 2] = double.NaN;
                    break;
                case RobeChannel.ROBECHANNEL_ISCOPY:
                    ReadPixel(data, pixelStart, setBytes, dest, destPos);
                    break;
            }
        }
    }

    public class MorphMap
    {
        uint mapWidth;
        uint mapHeight;
        ShapeOrNormals shapeOrNormals;
        uint minCol;
        uint maxCol;
        uint minRow;
        uint maxRow;
        Vector3[][] skinDeltas;
        Vector3[][] robeDeltas;

        public uint MapWidth  => this.mapWidth;
        public uint MapHeight => this.mapHeight;
        public uint MinCol    => this.minCol;
        public uint MaxCol    => this.maxCol;
        public uint MinRow    => this.minRow;
        public uint MaxRow    => this.maxRow;

        public float weight = 1f;
        public AgeGender age;
        public string package;
        public ulong instance;
        public SimRegion region;

        public Vector3 GetAdjustedDelta(int x, int y, bool mirrorX, byte robeBlend)
        {
            if (y < 0 || y >= skinDeltas.Length) return new Vector3();
            if (Math.Abs(x) >= skinDeltas[y].Length) return new Vector3();
            float robeMult = (float)robeBlend / 63f;
            Vector3 tmpS = new Vector3(skinDeltas[y][Math.Abs(x)]);
            if (mirrorX || x < 0) tmpS.X = -tmpS.X;
            Vector3 tmpR = new Vector3(robeDeltas[y][Math.Abs(x)]);
            if (mirrorX || x < 0) tmpR.X = -tmpR.X;
            Vector3 delta;
            delta = new Vector3((tmpS.X * (1f - robeMult)) + (tmpR.X * robeMult),
                                (tmpS.Y * (1f - robeMult)) + (tmpR.Y * robeMult),
                                (tmpS.Z * (1f - robeMult)) + (tmpR.Z * robeMult));
            return delta;
        }

        public bool HasData => this.skinDeltas.Length > 0;

        public MorphMap(uint mapWidth, uint mapHeight, ShapeOrNormals shapeOrNormals, uint minCol, uint maxCol,
                        uint minRow, uint maxRow, Vector3[][] skinDeltas, Vector3[][] robeDeltas, bool[] robeDataRow, float weight, AgeGender age,
                        string package, ulong instance, SimRegion region)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.shapeOrNormals = shapeOrNormals;
            this.minCol = minCol;
            this.maxCol = maxCol;
            this.minRow = minRow;
            this.maxRow = maxRow;
            this.skinDeltas = skinDeltas;
            this.robeDeltas = robeDeltas;
            this.weight = weight;
            this.age = age;
            this.package = package;
            this.instance = instance;
            this.region = region;
        }
    }
}
