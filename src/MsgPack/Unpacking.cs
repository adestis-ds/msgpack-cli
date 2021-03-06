﻿#region -- License Terms --
//
// MessagePack for CLI
//
// Copyright (C) 2010-2012 FUJIWARA, Yusuke
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//
#endregion -- License Terms --

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MsgPack
{
	// For integer direct conversions, see Unpackaging.Integers.tt & .cs.
	// For other direct conversions, see Unpackaging.Others.tt & .cs.
	// For string convenient APIs, see Unpackaging.String.cs.
	// This file defines common utility and indirect conversion.

	/// <summary>
	///		Defines direct conversion value from/to Message Pack binary stream without intermediate <see cref="MessagePackObject"/>.
	/// </summary>
	/// <remarks>
	///		This class provides convinient way to unpack objects from wellknown seekable stream.
	///		This class does not support stream feeding.
	/// </remarks>
	/// <seealso cref="Unpacker"/>
	public static partial class Unpacking
	{
		internal static readonly MessagePackObject[] PositiveIntegers =
			Enumerable.Range( 0, 0x80 ).Select( i => new MessagePackObject( unchecked( ( byte )i ) ) ).ToArray();
		internal static readonly MessagePackObject[] NegativeIntegers =
			Enumerable.Range( 0, 0x20 ).Select( i => new MessagePackObject( unchecked( ( sbyte )( -32 + i ) ) ) ).ToArray();

		internal static readonly MessagePackObject TrueValue = true;
		internal static readonly MessagePackObject FalseValue = false;

		internal static readonly MessagePackObject?[] NullablePositiveIntegers =
			Enumerable.Range( 0, 0x80 ).Select( i => new MessagePackObject?( unchecked( ( byte )i ) ) ).ToArray();
		internal static readonly MessagePackObject?[] NullableNegativeIntegers =
			Enumerable.Range( 0, 0x20 ).Select( i => new MessagePackObject?( unchecked( ( sbyte )( -32 + i ) ) ) ).ToArray();

		internal static readonly MessagePackObject? NullableTrueValue = true;
		internal static readonly MessagePackObject? NullableFalseValue = false;
		internal static readonly MessagePackObject? NullableNilValue = MessagePackObject.Nil;

		private static void ValidateByteArray( byte[] source, int offset )
		{
			if ( source == null )
			{
				throw new ArgumentNullException( "source" );
			}

			if ( source.Length == 0 )
			{
				throw new ArgumentException( "Source array is empty.", "source" );
			}

			if ( offset < 0 )
			{
				throw new ArgumentOutOfRangeException( "offset", "The offset cannot be negative." );
			}

			if ( source.Length <= offset )
			{
				throw new ArgumentException( "Source array is too small to the offset.", "source" );
			}
		}

		private static void ValidateStream( Stream source )
		{
			if ( source == null )
			{
				throw new ArgumentNullException( "source" );
			}

			if ( !source.CanRead )
			{
				throw new ArgumentException( "Stream is not readable.", "source" );
			}
		}

		private static void UnpackOne( Unpacker unpacker )
		{
			if ( !unpacker.Read() || !unpacker.Data.HasValue )
			{
				throw new UnpackException( "Cannot unpack MesssagePack object from the stream." );
			}
		}

		private static void VerifyIsScalar( Unpacker unpacker )
		{
			if ( unpacker.IsArrayHeader || unpacker.IsMapHeader )
			{
				throw new MessageTypeException( "The underlying stream is not scalar type." );
			}
		}


		private static bool UnpackBooleanCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				UnpackOne( unpacker );
				VerifyIsScalar( unpacker );
				try
				{
					return ( bool )unpacker.Data.Value;
				}
				catch ( InvalidOperationException ex )
				{
					throw NewTypeMismatchException( typeof( bool ), ex );
				}
			}
		}


		private static object UnpackNullCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				UnpackOne( unpacker );
				VerifyIsScalar( unpacker );

				if ( !unpacker.Data.Value.IsNil )
				{
					throw new MessageTypeException( "The underlying stream is not nil." );
				}

				return null;
			}
		}


		private static uint? UnpackArrayLengthCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				UnpackOne( unpacker );
				if ( IsNil( unpacker ) )
				{
					return null;
				}

				if ( !unpacker.IsArrayHeader )
				{
					throw new MessageTypeException( "The underlying stream is not array type." );
				}

				return ( uint )unpacker.Data.Value;
			}
		}

		private static IList<MessagePackObject> UnpackArrayCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				UnpackOne( unpacker );

				if ( !IsNil( unpacker ) && !unpacker.IsArrayHeader )
				{
					throw new MessageTypeException( "The underlying stream is not array type." );
				}

				return UnpackArrayCore( unpacker );
			}
		}

		private static IList<MessagePackObject> UnpackArrayCore( Unpacker unpacker )
		{
			if ( IsNil( unpacker ) )
			{
				return null;
			}

			uint length = ( uint )unpacker.Data.Value;
			if ( length > Int32.MaxValue )
			{
				throw new MessageNotSupportedException( "The array which length is greater than Int32.MaxValue is not supported." );
			}

			var result = new MessagePackObject[ unchecked( ( int )length ) ];
			for ( int i = 0; i < result.Length; i++ )
			{
				result[ i ] = UnpackObjectCore( unpacker );
			}

			return result;
		}


		private static uint? UnpackDictionaryCountCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				UnpackOne( unpacker );
				if ( IsNil( unpacker ) )
				{
					return null;
				}

				if ( !unpacker.IsMapHeader )
				{
					throw new MessageTypeException( "The underlying stream is not map type." );
				}

				return ( uint )unpacker.Data.Value;
			}
		}

		private static MessagePackObjectDictionary UnpackDictionaryCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				UnpackOne( unpacker );
				if ( !IsNil( unpacker ) && !unpacker.IsMapHeader )
				{
					throw new MessageTypeException( "The underlying stream is not map type." );
				}

				return UnpackDictionaryCore( unpacker );
			}
		}

		private static MessagePackObjectDictionary UnpackDictionaryCore( Unpacker unpacker )
		{
			if ( IsNil( unpacker ) )
			{
				return null;
			}


			uint count = ( uint )unpacker.Data.Value;
			if ( count > Int32.MaxValue )
			{
				throw new MessageNotSupportedException( "The map which count is greater than Int32.MaxValue is not supported." );
			}

			var result = new MessagePackObjectDictionary( unchecked( ( int )count ) );
			for ( int i = 0; i < count; i++ )
			{
				var key = UnpackObjectCore( unpacker );
				var value = UnpackObjectCore( unpacker );
				try
				{
					result.Add( key, value );
				}
				catch ( ArgumentException ex )
				{
					throw new InvalidMessagePackStreamException( "The dicationry key is duplicated in the stream.", ex );
				}
			}

			return result;
		}


		private static uint UnpackRawLengthCore( Stream source )
		{
			int header = source.ReadByte();
			if ( header < 0 )
			{
				throw new UnpackException( "Stream is end." );
			}

			if ( header == MessagePackCode.NilValue )
			{
				// Nil should be as empty stream because returning byte stream should not be null.
				return 0;
			}
			else if ( MessagePackCode.MinimumFixedRaw <= header && header <= MessagePackCode.MaximumFixedRaw )
			{
				return unchecked( ( uint )( header - MessagePackCode.MinimumFixedRaw ) );
			}
			else if ( header == MessagePackCode.Raw16 )
			{
				var bytes = ReadBytes( source, sizeof( short ) );
				unchecked
				{
					ushort buffer = bytes[ 1 ];
					buffer |= ( ushort )( bytes[ 0 ] << 8 );
					return buffer;
				}
			}
			else if ( header == MessagePackCode.Raw32 )
			{
				var bytes = ReadBytes( source, sizeof( int ) );
				unchecked
				{
					uint buffer = bytes[ 3 ];
					buffer |= ( uint )( bytes[ 2 ] << 8 );
					buffer |= ( uint )( bytes[ 1 ] << 16 );
					buffer |= ( uint )( bytes[ 0 ] << 24 );
					return buffer;
				}
			}
			else
			{
				throw new MessageTypeException( "The underlying stream is not raw type." );
			}
		}

		private static byte[] ReadBytes( Stream source, int length )
		{
			byte[] result = new byte[ length ];
			int bytes = source.Read( result, 0, length );
			if ( bytes < length )
			{
				throw new UnpackException( "The underlying stream unexpectedly ends." );
			}

			return result;
		}


		private static byte[] UnpackBinaryCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				UnpackOne( unpacker );
				try
				{
					return unpacker.Data.Value.AsBinary();
				}
				catch ( InvalidOperationException ex )
				{
					throw NewTypeMismatchException( typeof( byte[] ), ex );
				}
			}
		}


		private static MessagePackObject UnpackObjectCore( Stream source )
		{
			using ( var unpacker = Unpacker.Create( source, false ) )
			{
				return UnpackObjectCore( unpacker );
			}
		}

		private static MessagePackObject UnpackObjectCore( Unpacker unpacker )
		{
			UnpackOne( unpacker );

			if ( unpacker.IsArrayHeader )
			{
				return new MessagePackObject( UnpackArrayCore( unpacker ), true );
			}
			else if ( unpacker.IsMapHeader )
			{
				return new MessagePackObject( UnpackDictionaryCore( unpacker ), true );
			}
			else
			{
				return unpacker.Data.Value;
			}
		}

		private static bool IsNil( Unpacker unpacker )
		{
			return unpacker.Data.HasValue && unpacker.Data.Value.IsNil;
		}

		private static Exception NewTypeMismatchException( Type requestedType, InvalidOperationException innerException )
		{
			return new MessageTypeException( String.Format( CultureInfo.CurrentCulture, "Message type is not compatible to {0}.", requestedType ), innerException );
		}
	}
}
