﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Data;
using System.Runtime.Serialization;

namespace JocysCom.ClassLibrary.Runtime
{
	public partial class Helper
	{

		public static bool IsKnownType(Type type)
		{
			return
				type == typeof(string)
				|| type.IsPrimitive
				|| type.IsSerializable;
		}

		private static Type GetFirstArgumentOfGenericType(Type type)
		{
			return type.GetGenericArguments()[0];
		}

		public static bool IsNullableType(Type type)
		{
			return type.IsGenericType
				? type.GetGenericTypeDefinition() == typeof(Nullable<>)
				: false;
		}

		public static BindingFlags DefaultBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		/// <summary>
		/// Get source intersecting fields.
		/// </summary>
		private static FieldInfo[] GetItersectingFields(object source, object dest)
		{
			string[] dFieldNames = dest.GetType().GetFields(DefaultBindingFlags).Select(x => x.Name).ToArray();
			FieldInfo[] itersectingFields = source
				.GetType()
				.GetFields(DefaultBindingFlags)
				.Where(x => dFieldNames.Contains(x.Name))
				.ToArray();
			return itersectingFields;
		}

		public static void CopyFields(object source, object dest)
		{
			// Get type of the destination object.
			Type destType = dest.GetType();
			// Copy fields.
			FieldInfo[] sourceItersectingFields = GetItersectingFields(source, dest);
			foreach (FieldInfo sfi in sourceItersectingFields)
			{
				if (IsKnownType(sfi.FieldType))
				{
					FieldInfo dfi = destType.GetField(sfi.Name, DefaultBindingFlags);
					dfi.SetValue(dest, sfi.GetValue(source));
				}
			}
		}

		#region CopyProperties

		static object PropertiesReadLock = new object();
		static Dictionary<Type, PropertyInfo[]> PropertiesReadList = new Dictionary<Type, PropertyInfo[]>();
		static object PropertiesWriteLock = new object();
		static Dictionary<Type, PropertyInfo[]> PropertiesWriteList = new Dictionary<Type, PropertyInfo[]>();

		/// <summary>
		/// Get properties which exists on both objects.
		/// </summary>
		private static PropertyInfo[] GetItersectingProperties(object source, object dest)
		{
			// Properties to read.
			PropertyInfo[] sProperties;
			lock (PropertiesReadLock)
			{
				var sType = source.GetType();
				if (PropertiesReadList.ContainsKey(sType))
				{
					sProperties = PropertiesReadList[sType];
				}
				else
				{
					sProperties = sType.GetProperties(DefaultBindingFlags)
						.Where(p => Attribute.IsDefined(p, typeof(DataMemberAttribute)) && p.CanRead)
						.ToArray();
					PropertiesReadList.Add(sType, sProperties);
				}
			}
			// Properties to write.
			PropertyInfo[] dProperties;
			lock (PropertiesWriteLock)
			{
				var dType = dest.GetType();
				if (PropertiesWriteList.ContainsKey(dType))
				{
					dProperties = PropertiesWriteList[dType];
				}
				else
				{
					dProperties = dType.GetProperties(DefaultBindingFlags)
						.Where(p => Attribute.IsDefined(p, typeof(DataMemberAttribute)) && p.CanWrite)
						.ToArray();
					PropertiesWriteList.Add(dType, dProperties);
				}
			}
			var dPropertyNames = dProperties.Select(x => x.Name).ToArray();
			var itersectingProperties = sProperties
				.Where(x => dPropertyNames.Contains(x.Name))
				.ToArray();
			return itersectingProperties;
		}

		public static void CopyProperties(object source, object dest)
		{
			// Get type of the destination object.
			Type destType = dest.GetType();
			// Copy properties.
			PropertyInfo[] sourceItersectingProperties = GetItersectingProperties(source, dest);
			foreach (PropertyInfo spi in sourceItersectingProperties)
			{
				if (IsKnownType(spi.PropertyType) && spi.CanWrite)
				{
					PropertyInfo dpi = destType.GetProperty(spi.Name, DefaultBindingFlags);
					if (dpi.CanWrite) dpi.SetValue(dest, spi.GetValue(source, null), null);
				}
			}
		}

		#endregion

		#region CopyDataMembers

		static object DataMembersLock = new object();
		static Dictionary<Type, PropertyInfo[]> DataMembers = new Dictionary<Type, PropertyInfo[]>();

		/// <summary>
		/// Copy properties with [DataMemberAttribute].
		/// Only members declared at the level of the supplied type's hierarchy should be copied.
		/// Inherited members are not copied.
		/// </summary>
		public static void CopyDataMembers<T>(T source, T dest)
		{
			Type t = typeof(T);
			PropertyInfo[] ps;
			lock (DataMembersLock)
			{
				if (DataMembers.ContainsKey(t))
				{
					ps = DataMembers[t];
				}
				else
				{
					ps = t.GetProperties(DefaultBindingFlags | BindingFlags.DeclaredOnly)
						.Where(p => Attribute.IsDefined(p, typeof(DataMemberAttribute)) && p.CanRead && p.CanWrite)
						.ToArray();
					DataMembers.Add(t, ps);
				}
			}
			foreach (PropertyInfo p in ps)
			{
				p.SetValue(dest, p.GetValue(source, null), null);
			}
		}

		#endregion

		public static object CloneObject(object o)
		{
			Type t = o.GetType();
			PropertyInfo[] properties = t.GetProperties();
			object dest = t.InvokeMember("", BindingFlags.CreateInstance, null, o, null);
			foreach (PropertyInfo pi in properties)
			{
				if (pi.CanWrite) pi.SetValue(dest, pi.GetValue(o, null), null);
			}
			return dest;
		}

		/// <summary>
		/// Get change state by comparing old and new values.
		/// </summary>
		/// <param name="type">Type of values.</param>
		/// <param name="oldValue">Old value.</param>
		/// <param name="newValue">New value.</param>
		/// <returns></returns>
		public static ChangeState GetValueChangeSet(Type type, object oldValue, object newValue)
		{
			ChangeState state = new ChangeState();
			state.ValueType = type;
			state.oldValue = oldValue;
			state.newValue = newValue;
			state.State = System.Data.EntityState.Unchanged;
			if (oldValue != newValue) state.State = System.Data.EntityState.Modified;
			bool oldIsEmpty = oldValue == null || oldValue == Activator.CreateInstance(type);
			bool newIsEmpty = newValue == null || newValue == Activator.CreateInstance(type);
			if (oldIsEmpty && !newIsEmpty) state.State = System.Data.EntityState.Added;
			if (newIsEmpty && !oldIsEmpty) state.State = System.Data.EntityState.Deleted;
			return state;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="item1"></param>
		/// <param name="item2"></param>
		/// <returns></returns>
		public static System.Data.EntityState GetClassChangeState(object item1, object item2)
		{
			List<ChangeState> list = Helper.CompareProperties(item1, item2);
			EntityState state = EntityState.Unchanged;
			List<EntityState> states = list.Select(x => x.State).Distinct().ToList();
			states.Remove(EntityState.Unchanged);
			if (states.Count == 0) return state;
			if (states.Count == 1) return states[0];
			return EntityState.Modified;
		}

		/// <summary>
		/// Compare all properties of two objects and return change state for every property and field.
		/// </summary>
		/// <param name="source">item1</param>
		/// <param name="dest">item2</param>
		/// <returns></returns>
		public static List<ChangeState> CompareProperties(object source, object dest)
		{
			object oldValue;
			object newValue;
			ChangeState state;
			List<ChangeState> list = new List<ChangeState>();
			Type dstType = dest.GetType();
			FieldInfo[] itersectingFields = GetItersectingFields(source, dest);
			foreach (FieldInfo fi in itersectingFields)
			{
				FieldInfo dp = dstType.GetField(fi.Name);
				if (IsKnownType(fi.FieldType))
				{
					oldValue = fi.GetValue(source);
					newValue = fi.GetValue(dest);
					state = GetValueChangeSet(fi.FieldType, oldValue, newValue);
					list.Add(state);
				}
			}
			PropertyInfo[] itersectingProperties = GetItersectingProperties(source, dest);
			foreach (PropertyInfo pi in itersectingProperties)
			{
				PropertyInfo dp = dstType.GetProperty(pi.Name);
				if (IsKnownType(pi.PropertyType))
				{
					oldValue = pi.GetValue(source, null);
					newValue = pi.GetValue(dest, null);
					state = GetValueChangeSet(pi.PropertyType, oldValue, newValue);
					list.Add(state);
				}
			}
			return list;
		}



	}
}
