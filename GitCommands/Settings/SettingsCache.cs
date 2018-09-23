﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace GitCommands
{
    public abstract class SettingsCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, object> _byNameMap = new ConcurrentDictionary<string, object>();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void LockedAction(Action action)
        {
            LockedAction<object>(() =>
            {
                action();
                return null;
            });
        }

        protected T LockedAction<T>(Func<T> action)
        {
            lock (_byNameMap)
            {
                return action();
            }
        }

        protected T Action<T>(Func<T> action)
        {
            return action();
        }

        protected abstract void SaveImpl();
        protected abstract void LoadImpl();
        protected abstract void SetValueImpl(string key, string value);
        protected abstract string GetValueImpl(string key);
        protected abstract bool NeedRefresh();
        protected abstract void ClearImpl();

        private void Clear()
        {
            LockedAction(() =>
            {
                ClearImpl();
                _byNameMap.Clear();
            });
        }

        public void Save()
        {
            LockedAction(SaveImpl);
        }

        private void Load()
        {
            LockedAction(() =>
                {
                    Clear();
                    LoadImpl();
                });
        }

        public void Import(IEnumerable<(string name, string value)> keyValuePairs)
        {
            LockedAction(() =>
                {
                    foreach (var (key, value) in keyValuePairs)
                    {
                        if (value != null)
                        {
                            SetValueImpl(key, value);
                        }
                    }

                    Save();
                });
        }

        protected void EnsureSettingsAreUpToDate()
        {
            if (NeedRefresh())
            {
                LockedAction(Load);
            }
        }

        protected virtual void SettingsChanged()
        {
        }

        private void SetValue(string name, string value)
        {
            LockedAction(() =>
            {
                // will refresh EncodedNameMap if needed
                string inMemValue = GetValue(name);

                if (string.Equals(inMemValue, value))
                {
                    return;
                }

                SetValueImpl(name, value);

                SettingsChanged();
            });
        }

        private string GetValue(string name)
        {
            return Action(() =>
            {
                EnsureSettingsAreUpToDate();
                return GetValueImpl(name);
            });
        }

        public bool HasValue(string name)
        {
            return GetValue(name) != null;
        }

        public bool HasADifferentValue<T>(string name, T value, Func<T, string> encode)
        {
            var s = value != null
                ? encode(value)
                : null;

            return Action(() =>
            {
                string inMemValue = GetValue(name);
                return inMemValue != null && !string.Equals(inMemValue, s);
            });
        }

        public void SetValue<T>(string name, T value, Func<T, string> encode)
        {
            var s = value != null
                ? encode(value)
                : null;

            LockedAction(() =>
            {
                SetValue(name, s);

                object newValue = s == null ? (object)null : value;
                _byNameMap.AddOrUpdate(name, newValue, (key, oldValue) => newValue);
            });
        }

        public bool TryGetValue<T>([NotNull] string name, T defaultValue, [NotNull] Func<string, T> decode, out T value)
        {
            T val = defaultValue;

            bool result = Action(() =>
            {
                EnsureSettingsAreUpToDate();

                if (_byNameMap.TryGetValue(name, out object o))
                {
                    switch (o)
                    {
                        case null:
                            return false;
                        case T t:
                            val = t;
                            return true;
                        default:
                            throw new Exception("Incompatible class for settings: " + name + ". Expected: " + typeof(T).FullName + ", found: " + o.GetType().FullName);
                    }
                }

                if (decode == null)
                {
                    throw new ArgumentNullException(nameof(decode), string.Format("The decode parameter for setting {0} is null.", name));
                }

                string s = GetValue(name);

                if (s == null)
                {
                    val = defaultValue;
                    return false;
                }

                val = decode(s);
                _byNameMap.AddOrUpdate(name, val, (key, oldValue) => val);
                return true;
            });
            value = val;
            return result;
        }
    }
}
