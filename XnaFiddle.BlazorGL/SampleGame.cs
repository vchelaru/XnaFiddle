using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace XnaFiddle
{
    public class SampleGame : Game
    {
        private GraphicsDeviceManager graphics;
        private float hue;

        public SampleGame()
        {
            graphics = new GraphicsDeviceManager(this);
            graphics.GraphicsProfile = GraphicsProfile.HiDef;
            IsMouseVisible = true;
            Window.AllowUserResizing = true;
        }

        protected override void Initialize()
        {
            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            hue += (float)gameTime.ElapsedGameTime.TotalSeconds * 0.1f;
            if (hue > 1f) hue -= 1f;

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // Cycle through colors to show the game loop is running
            float r = MathF.Abs(MathF.Sin(hue * MathF.PI * 2f));
            float g = MathF.Abs(MathF.Sin((hue + 0.33f) * MathF.PI * 2f));
            float b = MathF.Abs(MathF.Sin((hue + 0.66f) * MathF.PI * 2f));
            GraphicsDevice.Clear(new Color(r * 0.4f, g * 0.4f, b * 0.4f));

            base.Draw(gameTime);
        }
    }
}
