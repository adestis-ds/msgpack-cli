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
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MsgPack.Serialization.Reflection;

namespace MsgPack.Serialization.EmittingSerializers
{
	/// <summary>
	///		Implements common features code generation based serializer builders.
	/// </summary>
	/// <typeparam name="TObject">The type of the serialization target.</typeparam>
	internal abstract class EmittingSerializerBuilder<TObject> : SerializerBuilder<TObject>
	{
		private readonly EmitterFlavor _emitterFlavor;

		private SerializationMethodGeneratorManager _generatorManager;

		internal SerializationMethodGeneratorManager GeneratorManager
		{
			get { return this._generatorManager; }
			set { this._generatorManager = value; }
		}

		/// <summary>
		///		Initializes a new instance of the <see cref="EmittingSerializerBuilder&lt;TObject&gt;"/> class.
		/// </summary>
		/// <param name="context">The <see cref="SerializationContext"/>.</param>
		protected EmittingSerializerBuilder( SerializationContext context )
			: base( context )
		{
			this._emitterFlavor = context.EmitterFlavor;
			this._generatorManager = SerializationMethodGeneratorManager.Get( context.GeneratorOption );
		}

		/// <summary>
		///		Creates serializer for <typeparamref name="TObject"/>.
		/// </summary>
		/// <param name="entries">Serialization target members. This will not be <c>null</c> nor empty.</param>
		/// <returns>
		///		<see cref="MessagePackSerializer{T}"/>. This value will not be <c>null</c>.
		/// </returns>
		protected sealed override MessagePackSerializer<TObject> CreateSerializer( SerializingMember[] entries )
		{
			using ( var emitter = this._generatorManager.CreateEmitter( typeof( TObject ), this._emitterFlavor ) )
			{
				try
				{
					var packerIL = emitter.GetPackToMethodILGenerator();
					try
					{
						if ( typeof( IPackable ).IsAssignableFrom( typeof( TObject ) ) )
						{
							if ( typeof( TObject ).IsValueType )
							{
								packerIL.EmitAnyLdarga( 2 );
							}
							else
							{
								packerIL.EmitAnyLdarg( 2 );
							}

							packerIL.EmitAnyLdarg( 1 );
							packerIL.EmitLdnull();

							packerIL.EmitCall( typeof( TObject ).GetInterfaceMap( typeof( IPackable ) ).TargetMethods.Single() );
							packerIL.EmitRet();
						}
						else
						{
							this.EmitPackMembers( emitter, packerIL, entries );
						}
					}
					finally
					{
						packerIL.FlushTrace();
					}

					var unpackerIL = emitter.GetUnpackFromMethodILGenerator();
					try
					{
						// TODO: For big struct, use Dictionary<String,SM>
						var result = unpackerIL.DeclareLocal( typeof( TObject ), "result" );
						Emittion.EmitConstruction( unpackerIL, result, null );

						if ( typeof( IUnpackable ).IsAssignableFrom( typeof( TObject ) ) )
						{
							if ( typeof( TObject ).GetIsValueType() )
							{
								unpackerIL.EmitAnyLdloca( result );
							}
							else
							{
								unpackerIL.EmitAnyLdloc( result );
							}

							unpackerIL.EmitAnyLdarg( 1 );
							unpackerIL.EmitCall( typeof( TObject ).GetInterfaceMap( typeof( IUnpackable ) ).TargetMethods.Single() );
						}
						else
						{
							EmitUnpackMembers( emitter, unpackerIL, entries, result );
						}

						unpackerIL.EmitAnyLdloc( result );
						unpackerIL.EmitRet();
					}
					finally
					{
						unpackerIL.FlushTrace();
					}

					return emitter.CreateInstance<TObject>( this.Context );
				}
				finally
				{
					emitter.FlushTrace();
				}
			}
		}

		/// <summary>
		///		Emits the ILs to pack the members of the current type.
		/// </summary>
		/// <param name="emitter"><see cref="SerializerEmitter"/> holding emittion context information.</param>
		/// <param name="packerIL"><see cref="TracingILGenerator"/> to emit IL.</param>
		/// <param name="entries">The array of <see cref="SerializingMember"/>s where each represents the member to be (de)serialized.</param>
		protected abstract void EmitPackMembers( SerializerEmitter emitter, TracingILGenerator packerIL, SerializingMember[] entries );

		private static void EmitUnpackMembers( SerializerEmitter emitter, TracingILGenerator unpackerIL, SerializingMember[] entries, LocalBuilder result )
		{
			/*
			 * #if T is IUnpackable
			 *  result.UnpackFromMessage( unpacker );
			 * #else
			 *	if( unpacker.IsArrayHeader )
			 *	{
			 *		...
			 *	}
			 *	else
			 *	{
			 *		...
			 *	}
			 * #endif
			 */

			var localHolder = new LocalVariableHolder( unpackerIL );

			unpackerIL.EmitAnyLdarg( 1 );
			unpackerIL.EmitGetProperty( Metadata._Unpacker.IsArrayHeader );
			var @else = unpackerIL.DefineLabel( "ELSE" );
			var endif = unpackerIL.DefineLabel( "END_IF" );
			unpackerIL.EmitBrfalse( @else );
			EmitUnpackMembersFromArray( emitter, unpackerIL, entries, result, localHolder );
			unpackerIL.EmitBr( endif );
			unpackerIL.MarkLabel( @else );
			EmitUnpackMembersFromMap( emitter, unpackerIL, entries, result, localHolder );
			unpackerIL.MarkLabel( endif );
		}

		private static void EmitUnpackMembersFromArray( SerializerEmitter emitter, TracingILGenerator unpackerIL, SerializingMember[] entries, LocalBuilder result, LocalVariableHolder localHolder )
		{
			/*
			 *  int unpacked = 0;
			 *  int itemsCount = unpacker.ItemsCount;
			 * 
			 *  :
			 *  if( unpacked == itemsCount )
			 *  {
			 *		HandleNilImplication(...);
			 *  }
			 *  else
			 *  {
			 *  #if PRIMITIVE
			 *		if( !unpacker.ReadT( out local1 ) )
			 *		{
			 *			throw SerializationExceptions.NewUnexpectedEndOfStreamMethod();
			 *		}
			 *  #else
			 *		if( !unpacker.Read() )
			 *		{
			 *			throw SerializationExceptions.NewUnexpectedEndOfStreamMethod();
			 *		}
			 *		
			 *		local1 = this._serializer1.Unpack
			 *	#endif
			 *		unpacked++;
			 *	}
			 *	:
			 */

			// TODO: Supports ExtensionObject like round-tripping.

			var itemsCount = localHolder.ItemsCount;
			var unpacked = unpackerIL.DeclareLocal( typeof( int ), "unpacked" );
			Emittion.EmitGetUnpackerItemsCountAsInt32( unpackerIL, 1, localHolder );
			unpackerIL.EmitAnyStloc( itemsCount );

			for ( int i = 0; i < entries.Length; i++ )
			{
				var endIf0 = unpackerIL.DefineLabel( "END_IF" );
				var else0 = unpackerIL.DefineLabel( "ELSE" );

				unpackerIL.EmitAnyLdloc( unpacked );
				unpackerIL.EmitAnyLdloc( itemsCount );
				unpackerIL.EmitBlt( else0 );
				// Tail missing member handling.
				if ( entries[ i ].Member != null )
				{
					// Respect nil implication.
					Emittion.EmitNilImplication( unpackerIL, 1, entries[ i ].Contract.Name, entries[ i ].Contract.NilImplication, endIf0, localHolder );
				}

				unpackerIL.EmitBr( endIf0 );

				unpackerIL.MarkLabel( else0 );

				if ( entries[ i ].Member == null )
				{
					Emittion.EmitGeneralRead( unpackerIL, 1 );
					// Ignore undefined member -- Nop.
				}
				else if ( UnpackHelpers.IsReadOnlyAppendableCollectionMember( entries[ i ].Member ) )
				{
					Emittion.EmitDeserializeCollectionValue(
						emitter,
						unpackerIL,
						1,
						result,
						entries[ i ].Member,
						entries[ i ].Member.GetMemberValueType(),
						entries[ i ].Contract.NilImplication,
						localHolder 
					);
				}
				else
				{
					Emittion.EmitDeserializeValue(
						emitter,
						unpackerIL,
						1,
						result,
						entries[ i ],
						localHolder
					);
				}

				unpackerIL.EmitAnyLdloc( unpacked );
				unpackerIL.EmitLdc_I4_1();
				unpackerIL.EmitAdd();
				unpackerIL.EmitAnyStloc( unpacked );

				unpackerIL.MarkLabel( endIf0 );
			}
		}

		private static void EmitUnpackMembersFromMap( SerializerEmitter emitter, TracingILGenerator unpackerIL, SerializingMember[] entries, LocalBuilder result, LocalVariableHolder localHolder )
		{
			/*
			 *		var memberName = unpacker.Data.AsString();
			 *		if( memberName == "A" )
			 *		{
			 *			if( !unpacker.Read() )
			 *			{
			 *				throw SerializationExceptions.NewUnexpectedStreamEndsException();
			 *			}
			 *			
			 *			isAFound = true;
			 *		}
			 *		:
			 */

			var beginLoop = unpackerIL.DefineLabel( "BEGIN_LOOP" );
			var endLoop = unpackerIL.DefineLabel( "END_LOOP" );
			unpackerIL.MarkLabel( beginLoop );
			var memberName = localHolder.MemberName;
			unpackerIL.EmitAnyLdarg( 1 );
			unpackerIL.EmitAnyLdloca( memberName );
			unpackerIL.EmitAnyCall( Metadata._Unpacker.ReadString );
			unpackerIL.EmitBrfalse( endLoop );

			var data = localHolder.UnpackedData;
			var dataValue = localHolder.UnpackedDataValue;
			for ( int i = 0; i < entries.Length; i++ )
			{
				if ( entries[ i ].Contract.Name == null )
				{
					// skip undefined member
					continue;
				}

				// TODO: binary comparison

				// Is it current member?
				unpackerIL.EmitAnyLdloc( memberName );
				unpackerIL.EmitLdstr( entries[ i ].Contract.Name );
				unpackerIL.EmitAnyCall( Metadata._String.op_Inequality );
				var endIfCurrentMember = unpackerIL.DefineLabel( "END_IF_MEMBER_" + i );
				unpackerIL.EmitBrtrue( endIfCurrentMember );

				// Deserialize value
				if ( entries[ i ].Member == null )
				{
					Emittion.EmitGeneralRead( unpackerIL, 1 );
					// Ignore undefined member -- Nop.
				}
				else if ( UnpackHelpers.IsReadOnlyAppendableCollectionMember( entries[ i ].Member ) )
				{
					Emittion.EmitDeserializeCollectionValue(
						emitter,
						unpackerIL,
						1,
						result,
						entries[ i ].Member,
						entries[ i ].Member.GetMemberValueType(),
						entries[ i ].Contract.NilImplication,
						localHolder
					);
				}
				else
				{
					Emittion.EmitDeserializeValue(
						emitter,
						unpackerIL,
						1,
						result,
						entries[ i ],
						localHolder
					);
				}

				// TOOD: Record for missing check

				unpackerIL.EmitBr( beginLoop );
				unpackerIL.MarkLabel( endIfCurrentMember );
			}

			// TODO: Handle unknown member.
			unpackerIL.EmitBr( beginLoop );
			unpackerIL.MarkLabel( endLoop );
		}

		/// <summary>
		///		Creates serializer as <typeparamref name="TObject"/> is array type.
		/// </summary>
		/// <returns>
		///		<see cref="MessagePackSerializer{T}"/>. 
		///		This value will not be <c>null</c>.
		/// </returns>
		public sealed override MessagePackSerializer<TObject> CreateArraySerializer()
		{
			using ( var emitter = EmittingSerializerBuilderLogics.CreateArraySerializerCore( typeof( TObject ), this._emitterFlavor ) )
			{
				try
				{
					return emitter.CreateInstance<TObject>( this.Context );
				}
				finally
				{
					emitter.FlushTrace();
				}
			}
		}

		/// <summary>
		///		Creates serializer as <typeparamref name="TObject"/> is map type.
		/// </summary>
		/// <returns>
		///		<see cref="MessagePackSerializer{T}"/>. 
		///		This value will not be <c>null</c>.
		/// </returns>
		public sealed override MessagePackSerializer<TObject> CreateMapSerializer()
		{
			using ( var emitter = EmittingSerializerBuilderLogics.CreateMapSerializerCore( typeof( TObject ), this._emitterFlavor ) )
			{
				try
				{
					return emitter.CreateInstance<TObject>( this.Context );
				}
				finally
				{
					emitter.FlushTrace();
				}
			}
		}

		/// <summary>
		///		Creates serializer as <typeparamref name="TObject"/> is tuple type.
		/// </summary>
		/// <returns>
		///		<see cref="MessagePackSerializer{T}"/>. 
		///		This value will not be <c>null</c>.
		/// </returns>
		public sealed override MessagePackSerializer<TObject> CreateTupleSerializer()
		{
#if WINDOWS_PHONE || NETFX_35
			throw new PlatformNotSupportedException();
#else
			using ( var emitter = EmittingSerializerBuilderLogics.CreateTupleSerializerCore( typeof( TObject ), this._emitterFlavor ) )
			{
				try
				{
					return emitter.CreateInstance<TObject>( this.Context );
				}
				finally
				{
					emitter.FlushTrace();
				}
			}
#endif
		}
	}
}
