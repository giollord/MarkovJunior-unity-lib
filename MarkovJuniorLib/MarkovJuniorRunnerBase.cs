﻿using MarkovJuniorLib.Models;
using MarkovJuniorLib.Internal;
using MarkovJuniorLib.ToOverride;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Graphics = MarkovJuniorLib.Internal.Graphics;
using GUI = MarkovJuniorLib.Internal.GUI;

namespace MarkovJuniorLib
{
    /// <summary>
    /// Entry point to run MarkovJunior algorithm
    /// </summary>
    public abstract class MarkovJuniorRunnerBase<TTexture, TColor, TModelConfig, TRunResult> where TTexture : class where TModelConfig : ModelConfigBase<TTexture> where TRunResult:RunResult<TTexture>
    {
        /// <summary>
        /// Run algorithm lazily, so results will be generated during enumeration
        /// </summary>
        /// <param name="modelConfig">Configuration</param>
        /// <returns>Results of execution</returns>
        /// <exception cref="Exception">Throws exception if something goes wrong</exception>
        public abstract IEnumerable<TRunResult> Run(TModelConfig modelConfig);

        /// <summary>
        /// Get default pallette
        /// </summary>
        /// <returns>Dictionary with symbols and matching colors</returns>
        public Dictionary<char, TColor> GetDefaultPallette()
        {
            return GetDefaultPallettePrivate().ToDictionary(x => x.Key, x => ConvertColor(x.Value));
        }

        protected abstract TColor ConvertColor(Color32 c);

        /// <summary>
        /// Get default pallette
        /// </summary>
        /// <returns>Dictionary with symbols and matching colors</returns>
        private Dictionary<char, Color32> GetDefaultPallettePrivate()
        {
            static byte GetColorComp(int val, int comp) => (byte)((val >> (comp * 8)) & 255);

            using var palletteTextReader = new System.IO.StringReader(Constants.Pallette_XML);
            Dictionary<char, Color32> pallette = XDocument.Load(palletteTextReader).Root.Elements("color").ToDictionary(
                x => x.Get<char>("symbol"),
                x =>
                {
                    var i = (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16);
                    return new Color32(GetColorComp(i, 2), GetColorComp(i, 1), GetColorComp(i, 0), GetColorComp(i, 3));
                });
            return pallette;
        }

        /// <summary>
        /// Run algorithm lazily, so results will be generated during enumeration
        /// </summary>
        /// <param name="modelConfig">Configuration</param>
        /// <returns>Results of execution</returns>
        /// <exception cref="Exception">Throws exception if something goes wrong</exception>
        protected IEnumerable<RunResult> Run(ModelConfigBase<TTexture> modelConfig, IRandom random, ITextureHelper textureHelper)
        {
            //Resources.palette
            var gui = new GUI(textureHelper);
            using var palletteTextReader = new System.IO.StringReader(Constants.Pallette_XML);
            Dictionary<char, int> palette = XDocument.Load(palletteTextReader).Root.Elements("color").ToDictionary(x => x.Get<char>("symbol"), x => (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16));

            using var modelTextReader = new System.IO.StringReader(modelConfig.ModelXML);
            XDocument modeldoc = XDocument.Load(modelTextReader, LoadOptions.SetLineInfo);

            Interpreter interpreter = Interpreter.Load(modelConfig, modeldoc.Root, modelConfig.Width_MX, modelConfig.Height_MY, modelConfig.Depth_MZ);
            if (interpreter == null)
            {
                throw new Exception("Can not create interpreter");
            }

            Dictionary<char, int> customPalette = new(palette);
            foreach (var x in modelConfig.Colors) customPalette[x.Symbol] = (x.Color.A << 24) + (x.Color.R << 16) + (x.Color.G << 8) + (x.Color.B);

            byte[] initialState = null;
            if(modelConfig.InitialGridInternal != null)
            {
                if (modelConfig.InitialGridInternal.GetLength(0) != modelConfig.Width_MX || modelConfig.InitialGridInternal.GetLength(1) != modelConfig.Height_MY || modelConfig.InitialGridInternal.GetLength(2) != modelConfig.Depth_MZ)
                    throw new Exception("Initial texture width and height must match MX and MY");

                var (initialColors, _, _, _) = Graphics.LoadGrid(modelConfig.InitialGridInternal);
                var colorIndexes = customPalette.ToDictionary(x => x.Value, x => (byte)Array.IndexOf(interpreter.grid.characters, x.Key));
                initialState = new byte[initialColors.Length];
                for (var i = 0; i < initialColors.Length; i++)
                {
                    initialState[i] = colorIndexes[initialColors[i]];
                }
            }

            var results = new List<RunResult>();
            for (int k = 0; k < modelConfig.Amount; k++)
            {
                int seed = modelConfig.Seeds != null && k < modelConfig.Seeds.Length ? modelConfig.Seeds[k] : random.Range(0, int.MaxValue);
                foreach ((byte[] result, char[] legend, int FX, int FY, int FZ) in interpreter.Run(seed, modelConfig.Steps, modelConfig.Gif, initialState))
                {
                    int[] colors = legend.Select(ch => customPalette[ch]).ToArray();
                    var runResult = new RunResult();
                    //string outputname = modelConfig.Gif ? $"output/{interpreter.counter}" : $"output/{modelConfig.Name}_{seed}";
                    if (modelConfig.Output2dTexture && (FZ == 1 || modelConfig.Iso))
                    {
                        var (bitmap, WIDTH, HEIGHT) = Graphics.Render(result, FX, FY, FZ, colors, modelConfig.PixelSize, modelConfig.Gui);
                        if (modelConfig.Gui > 0) gui.Draw(modelConfig.Name, interpreter.root, interpreter.current, bitmap, WIDTH, HEIGHT, customPalette);
                        var tex = Graphics.SaveBitmap(textureHelper, bitmap, WIDTH, HEIGHT);
                        runResult.Texture = tex;
                    }
                    if (modelConfig.Output3dVox)
                    {
                        var vox = VoxHelper.SaveVox(result, (byte)FX, (byte)FY, (byte)FZ, colors, null);
                        runResult.Vox = vox;
                    }
                    if (modelConfig.Output3dColors)
                    {
                        var out3dColors = VoxHelper.SaveColorsArray(result, FX, FY, FZ, colors);
                        runResult.Output3d = out3dColors;
                    }

                    yield return runResult;
                }
            }
        }
    }
}
