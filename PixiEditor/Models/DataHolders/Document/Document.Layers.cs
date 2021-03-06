﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using GalaSoft.MvvmLight.Messaging;
using PixiEditor.Models.Controllers;
using PixiEditor.Models.Enums;
using PixiEditor.Models.Layers;
using PixiEditor.Models.Position;
using PixiEditor.Models.Undo;

namespace PixiEditor.Models.DataHolders
{
    public partial class Document
    {
        public const string MainSelectedLayerColor = "#505056";
        public const string SecondarySelectedLayerColor = "#7D505056";
        private Guid activeLayerGuid;

        public ObservableCollection<Layer> Layers { get; set; } = new ObservableCollection<Layer>();

        public Layer ActiveLayer => Layers.Count > 0 ? Layers.FirstOrDefault(x => x.LayerGuid == ActiveLayerGuid) : null;

        public Guid ActiveLayerGuid
        {
            get => activeLayerGuid;
            set
            {
                activeLayerGuid = value;
                RaisePropertyChanged(nameof(ActiveLayerGuid));
                RaisePropertyChanged(nameof(ActiveLayer));
            }
        }

        public event EventHandler<LayersChangedEventArgs> LayersChanged;

        public void SetMainActiveLayer(int index)
        {
            if (ActiveLayer != null && Layers.IndexOf(ActiveLayer) <= Layers.Count - 1)
            {
                ActiveLayer.IsActive = false;
            }

            foreach (var layer in Layers)
            {
                if (layer.IsActive)
                {
                    layer.IsActive = false;
                }
            }

            ActiveLayerGuid = Layers[index].LayerGuid;
            ActiveLayer.IsActive = true;
            LayersChanged?.Invoke(this, new LayersChangedEventArgs(ActiveLayerGuid, LayerAction.SetActive));
        }

        public void UpdateLayersColor()
        {
            foreach (var layer in Layers)
            {
                if (layer.LayerGuid == ActiveLayerGuid)
                {
                    layer.LayerHighlightColor = MainSelectedLayerColor;
                }
                else
                {
                    layer.LayerHighlightColor = SecondarySelectedLayerColor;
                }
            }
        }

        public void MoveLayerIndexBy(int layerIndex, int amount)
        {
            MoveLayerProcess(new object[] { layerIndex, amount });

            UndoManager.AddUndoChange(new Change(
                MoveLayerProcess,
                new object[] { layerIndex + amount, -amount },
                MoveLayerProcess,
                new object[] { layerIndex, amount },
                "Move layer"));
        }

        public void AddNewLayer(string name, WriteableBitmap bitmap, bool setAsActive = true)
        {
            AddNewLayer(name, bitmap.PixelWidth, bitmap.PixelHeight, setAsActive);
            Layers.Last().LayerBitmap = bitmap;
        }

        public void AddNewLayer(string name, bool setAsActive = true)
        {
            AddNewLayer(name, 0, 0, setAsActive);
        }

        public void AddNewLayer(string name, int width, int height, bool setAsActive = true)
        {
            Layers.Add(new Layer(name, width, height)
            {
                MaxHeight = Height,
                MaxWidth = Width
            });
            if (setAsActive)
            {
                SetMainActiveLayer(Layers.Count - 1);
            }

            if (Layers.Count > 1)
            {
                StorageBasedChange storageChange = new StorageBasedChange(this, new[] { Layers[^1] }, false);
                UndoManager.AddUndoChange(
                    storageChange.ToChange(
                        RemoveLayerProcess,
                        new object[] { Layers[^1].LayerGuid },
                        RestoreLayersProcess,
                        "Add layer"));
            }

            LayersChanged?.Invoke(this, new LayersChangedEventArgs(Layers[0].LayerGuid, LayerAction.Add));
        }

        public void SetNextLayerAsActive(int lastLayerIndex)
        {
            if (Layers.Count > 0)
            {
                if (lastLayerIndex == 0)
                {
                    SetMainActiveLayer(0);
                }
                else
                {
                    SetMainActiveLayer(lastLayerIndex - 1);
                }
            }
        }

        public void SetNextSelectedLayerAsActive(Guid lastLayerGuid)
        {
            var selectedLayers = Layers.Where(x => x.IsActive);
            foreach (var layer in selectedLayers)
            {
                if (layer.LayerGuid != lastLayerGuid)
                {
                    ActiveLayerGuid = layer.LayerGuid;
                    LayersChanged?.Invoke(this, new LayersChangedEventArgs(ActiveLayerGuid, LayerAction.SetActive));
                    return;
                }
            }
        }

        public void ToggleLayer(int index)
        {
            if (index < Layers.Count && index >= 0)
            {
                Layer layer = Layers[index];
                if (layer.IsActive && Layers.Count(x => x.IsActive) == 1)
                {
                    return;
                }

                if (ActiveLayerGuid == layer.LayerGuid)
                {
                    SetNextSelectedLayerAsActive(layer.LayerGuid);
                }

                layer.IsActive = !layer.IsActive;
            }
        }

        /// <summary>
        /// Selects all layers between active layer and layer at given index.
        /// </summary>
        /// <param name="index">End of range index.</param>
        public void SelectLayersRange(int index)
        {
            DeselectAllExcept(ActiveLayer);
            int firstIndex = Layers.IndexOf(ActiveLayer);

            int startIndex = Math.Min(index, firstIndex);
            for (int i = startIndex; i <= startIndex + Math.Abs(index - firstIndex); i++)
            {
                Layers[i].IsActive = true;
            }
        }

        public void DeselectAllExcept(Layer exceptLayer)
        {
            foreach (var layer in Layers)
            {
                if (layer == exceptLayer)
                {
                    continue;
                }

                layer.IsActive = false;
            }
        }

        public void RemoveLayer(int layerIndex)
        {
            if (Layers.Count == 0)
            {
                return;
            }

            bool wasActive = Layers[layerIndex].IsActive;

            StorageBasedChange change = new StorageBasedChange(this, new[] { Layers[layerIndex] });
            UndoManager.AddUndoChange(
                change.ToChange(RestoreLayersProcess, RemoveLayerProcess, new object[] { Layers[layerIndex].LayerGuid }, "Remove layer"));

            Layers.RemoveAt(layerIndex);
            if (wasActive)
            {
                SetNextLayerAsActive(layerIndex);
            }
        }

        public void RemoveLayer(Layer layer)
        {
            RemoveLayer(Layers.IndexOf(layer));
        }

        public void RemoveActiveLayers()
        {
            if (Layers.Count == 0 || !Layers.Any(x => x.IsActive))
            {
                return;
            }

            Layer[] layers = Layers.Where(x => x.IsActive).ToArray();
            int firstIndex = Layers.IndexOf(layers[0]);

            object[] guidArgs = new object[] { layers.Select(x => x.LayerGuid).ToArray() };

            StorageBasedChange change = new StorageBasedChange(this, layers);

            RemoveLayersProcess(guidArgs);

            InjectRemoveActiveLayersUndo(guidArgs, change);

            SetNextLayerAsActive(firstIndex);
        }

        public Layer MergeLayers(Layer[] layersToMerge, bool nameOfLast, int index)
        {
            if (layersToMerge == null || layersToMerge.Length < 2)
            {
                throw new ArgumentException("Not enough layers were provided to merge. Minimum amount is 2");
            }

            string name;

            // Wich name should be used
            if (nameOfLast)
            {
                name = layersToMerge[^1].Name;
            }
            else
            {
                name = layersToMerge[0].Name;
            }

            Layer mergedLayer = layersToMerge[0];

            for (int i = 0; i < layersToMerge.Length - 1; i++)
            {
                Layer firstLayer = mergedLayer;
                Layer secondLayer = layersToMerge[i + 1];
                mergedLayer = firstLayer.MergeWith(secondLayer, name, Width, Height);
                Layers.Remove(layersToMerge[i]);
            }

            Layers.Remove(layersToMerge[^1]);

            Layers.Insert(index, mergedLayer);

            SetMainActiveLayer(Layers.IndexOf(mergedLayer));

            return mergedLayer;
        }

        public Layer MergeLayers(Layer[] layersToMerge, bool nameIsLastLayers)
        {
            if (layersToMerge == null || layersToMerge.Length < 2)
            {
                throw new ArgumentException("Not enough layers were provided to merge. Minimum amount is 2");
            }

            IEnumerable<Layer> undoArgs = layersToMerge;

            StorageBasedChange undoChange = new StorageBasedChange(this, undoArgs);

            int[] indexes = layersToMerge.Select(x => Layers.IndexOf(x)).ToArray();

            var layer = MergeLayers(layersToMerge, nameIsLastLayers, Layers.IndexOf(layersToMerge[0]));

            UndoManager.AddUndoChange(undoChange.ToChange(
                InsertLayersAtIndexesProcess,
                new object[] { indexes[0] },
                MergeLayersProcess,
                new object[] { indexes, nameIsLastLayers, layer.LayerGuid },
                "Undo merge layers"));

            return layer;
        }

        private void InjectRemoveActiveLayersUndo(object[] guidArgs, StorageBasedChange change)
        {
            Action<Layer[], UndoLayer[]> undoAction = RestoreLayersProcess;
            Action<object[]> redoAction = RemoveLayersProcess;

            if (Layers.Count == 0)
            {
                Layer layer = new Layer("Base Layer");
                Layers.Add(layer);
                undoAction = (Layer[] layers, UndoLayer[] undoData) =>
                {
                    Layers.RemoveAt(0);
                    RestoreLayersProcess(layers, undoData);
                };
                redoAction = (object[] args) =>
                {
                    RemoveLayersProcess(args);
                    Layers.Add(layer);
                };
            }

            UndoManager.AddUndoChange(
            change.ToChange(
                undoAction,
                redoAction,
                guidArgs,
                "Remove layers"));
        }

        private void MergeLayersProcess(object[] args)
        {
            if (args.Length > 0
                && args[0] is int[] indexes
                && args[1] is bool nameOfSecond
                && args[2] is Guid mergedLayerGuid)
            {
                Layer[] layers = new Layer[indexes.Length];

                for (int i = 0; i < layers.Length; i++)
                {
                    layers[i] = Layers[indexes[i]];
                }

                Layer layer = MergeLayers(layers, nameOfSecond, indexes[0]);
                layer.ChangeGuid(mergedLayerGuid);
            }
        }

        private void InsertLayersAtIndexesProcess(Layer[] layers, UndoLayer[] data, object[] args)
        {
            if (args.Length > 0 && args[0] is int layerIndex)
            {
                Layers.RemoveAt(layerIndex);
                for (int i = 0; i < layers.Length; i++)
                {
                    Layer layer = layers[i];
                    layer.IsActive = true;
                    Layers.Insert(data[i].LayerIndex, layer);
                }

                ActiveLayerGuid = layers.First(x => x.LayerHighlightColor == MainSelectedLayerColor).LayerGuid;
                // Identifying main layer by highlightColor is a bit hacky, but shhh
            }
        }

        /// <summary>
        ///     Moves offsets of layers by specified vector.
        /// </summary>
        private void MoveOffsets(IEnumerable<Layer> layers, Coordinates moveVector)
        {
            foreach (Layer layer in layers)
            {
                Thickness offset = layer.Offset;
                layer.Offset = new Thickness(offset.Left + moveVector.X, offset.Top + moveVector.Y, 0, 0);
            }
        }

        private void MoveOffsetsProcess(object[] arguments)
        {
            if (arguments.Length > 0 && arguments[0] is IEnumerable<Layer> layers && arguments[1] is Coordinates vector)
            {
                MoveOffsets(layers, vector);
            }
            else
            {
                throw new ArgumentException("Provided arguments were invalid. Expected IEnumerable<Layer> and Coordinates");
            }
        }

        private void MoveLayerProcess(object[] parameter)
        {
            int layerIndex = (int)parameter[0];
            int amount = (int)parameter[1];

            Layers.Move(layerIndex, layerIndex + amount);
            if (Layers.IndexOf(ActiveLayer) == layerIndex)
            {
                SetMainActiveLayer(layerIndex + amount);
            }
        }

        private void RestoreLayersProcess(Layer[] layers, UndoLayer[] layersData)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];

                Layers.Insert(layersData[i].LayerIndex, layer);
                if (layersData[i].IsActive)
                {
                    SetMainActiveLayer(Layers.IndexOf(layer));
                }
            }
        }

        private void RemoveLayerProcess(object[] parameters)
        {
            if (parameters != null && parameters.Length > 0 && parameters[0] is Guid layerGuid)
            {
                Layer layer = Layers.First(x => x.LayerGuid == layerGuid);
                int index = Layers.IndexOf(layer);
                bool wasActive = layer.IsActive;
                Layers.Remove(layer);

                if (wasActive || Layers.IndexOf(ActiveLayer) >= index)
                {
                    SetNextLayerAsActive(index);
                }
            }
        }

        private void RemoveLayersProcess(object[] parameters)
        {
            if (parameters != null && parameters.Length > 0 && parameters[0] is IEnumerable<Guid> layerGuids)
            {
                object[] args = new object[1];
                foreach (var guid in layerGuids)
                {
                    args[0] = guid;
                    RemoveLayerProcess(args);
                }
            }
        }
    }
}