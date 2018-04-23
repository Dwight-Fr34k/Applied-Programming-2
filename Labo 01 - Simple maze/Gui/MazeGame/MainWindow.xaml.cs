﻿using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace MazeGame
{
    public partial class MainWindow : Window
    {
        #region Private fields
        private static int ROUNDNESS = 20; // 1000 is too much - 4 is too little
        private static Color PLAYER_COLOR = Colors.Orange; // I like orange
        private static DateTime DATETIME_NULL = new DateTime(0);
        private OpenFileDialog ofd = new OpenFileDialog() { Filter = "json files (*.json)|*.json", RestoreDirectory = true }; // The OFD for Reading the Maze JSON's
        private Model3DGroup modelGrp = new Model3DGroup(); // The container for each element (cubes and light)
        private MazeData currentMazeData = new MazeData(); // The Maze data that's currently in use
        private Sphere player; // Player object
        private List<BlockType> blockTypes = new BlockType[]
        {
            new BlockType() { BlockCode = 'G', TexturePath = "Resources\\Ground.jpg" },
            new BlockType() { BlockCode = 'W', TexturePath = "Resources\\Wall.jpg" },
            new BlockType() { BlockCode = 'B', TexturePath = "Resources\\Base.jpg" },
            new BlockType() { BlockCode = 'F', TexturePath = "Resources\\Finish.jpg" }
        }.ToList(); // All types of possible blocks
        private bool[] tiltDirection = { false, false, false, false }; // Up,Right,Down,Left
        private Vector3D movementVector;
        private DispatcherTimer timer = new DispatcherTimer() { Interval = new TimeSpan(0, 0, 0, 0, 10) };
        #endregion Private fields

        #region Constructors
        public MainWindow()
        {
            InitializeComponent();
        }
        #endregion Constructors

        #region Events
        /// <summary>
        /// Basic on Load pre-setter
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // reset field
            ResetModel();

            // Model elements
            HelixViewport.Children.Add(new ModelVisual3D() { Content = modelGrp });

            // Camera setup
            ResetCamera();

            // Timer
            timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// Load data
        /// </summary>
        private void MenuItemOpen_Click(object sender, RoutedEventArgs e)
        {
            // File loading
            if (ofd.ShowDialog() == true)
            {
                // Reset the Model
                ResetModel();

                // Read data
                Stream myStream;
                MazeData readData = new MazeData();
                try
                {
                    if ((myStream = ofd.OpenFile()) != null) using (StreamReader r = new StreamReader(myStream)) readData = JsonConvert.DeserializeObject<MazeData>(r.ReadToEnd());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading the file! MSG>" + ex.Message);
                    return;
                }

                // Check data
                if (readData.CameraHeight == 0) // No camera Height
                {
                    Console.WriteLine("Error reading the file! (No CameraHeight)");
                    return;
                }
                if (readData.CodeSingle == null) // No CodeSingle
                {
                    Console.WriteLine("Error reading the file! (No code)");
                    return;
                }
                if (readData.Size == null) // No dimensions
                {
                    Console.WriteLine("Error reading the file! (No Size)");
                    return;
                }
                if (readData.Size.Count != 3) // Wrong # of dimensions
                {
                    Console.WriteLine("Error reading the file! (Wrong Size number, 3 expected)");
                    return;
                }
                if (readData.Size[0] * readData.Size[1] * readData.Size[2] != readData.CodeSingle.Length) // Dimensions and String not compatible
                {
                    Console.WriteLine("Error reading the file! (Size and Code do not match in number!) - Size:" + (readData.Size[0] * readData.Size[1] * readData.Size[2]) + " Code:" + readData.CodeSingle.Length);
                    return;
                }

                // Sync current
                currentMazeData = readData;

                // Set camera
                ResetCamera();

                // AddCubes
                AddCubesFromCurrentData();

                Console.WriteLine("Loaded in new data: " + currentMazeData.Title);
            }
        }

        /// <summary>
        /// Key Press
        /// </summary>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Update tilts
            switch (e.Key)
            {
                case Key.NumPad8:
                    tiltDirection[0] = true; // up
                    break;
                case Key.NumPad6:
                    tiltDirection[1] = true; // right
                    break;
                case Key.NumPad2:
                    tiltDirection[2] = true; // down
                    break;
                case Key.NumPad4:
                    tiltDirection[3] = true; // left
                    break;
                case Key.NumPad5:
                    ResetCamera(); // midle - Camera reset
                    break;
            }
        }

        /// <summary>
        /// Key Release
        /// </summary>
        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            // Update tilts
            switch (e.Key)
            {
                case Key.NumPad8:
                    tiltDirection[0] = false; // up
                    break;
                case Key.NumPad6:
                    tiltDirection[1] = false; // right
                    break;
                case Key.NumPad2:
                    tiltDirection[2] = false; // down
                    break;
                case Key.NumPad4:
                    tiltDirection[3] = false; // left
                    break;
            }
        }
        #endregion Events

        #region Private methods
        /// <summary>
        /// Empties the current model and re-adds the lights
        /// </summary>
        private void ResetModel()
        {
            // Empty everything and add simple light
            modelGrp.Children = new Model3DCollection { new DirectionalLight() { Color = Colors.White } };
        }

        /// <summary>
        /// Resets the camera back to starting position and height defined by CurrentMazData
        /// </summary>
        private void ResetCamera()
        {
            HelixViewport.Camera = new PerspectiveCamera() // OrthoCam is weird and feels unnatural
            {
                Position = new Point3D(0, 0, currentMazeData.CameraHeight),
                LookDirection = new Vector3D(0, 0, -1),
                UpDirection = new Vector3D(0, 1, 0)
            };
        }

        /// <summary>
        /// Uses CurrentMazeData to make cubes and adds it to the model
        /// </summary>
        private void AddCubesFromCurrentData()
        {
            // Set Limits, string and counter
            var X = currentMazeData.Size[0];
            var Y = currentMazeData.Size[1];
            var Z = currentMazeData.Size[2];
            var codeString = currentMazeData.CodeSingle;
            var counter = -1;

            // Loop over all Layers
            for (var z = -Z / 2; z < Z / 2; z++)
            {
                // Loop over all Rows
                for (var y = Y / 2; y > -Y / 2; y--)
                {
                    // Loop over all Cubes
                    for (var x = -X / 2; x < X / 2; x++)
                    {
                        counter++;
                        if (codeString[counter] == 'E') continue; // Empty block
                        modelGrp.Children.Add(new Block(new Point3D(x, y, z), blockTypes.Where(t => t.BlockCode == codeString[counter]).ToList()[0]).Model);
                    }
                }
            }

            // Add the player too
            AddPlayer();
            timer.Start();
        }

        private void AddPlayer()
        {
            // Remove the current player in the view
            if (player != null) modelGrp.Children.Remove(player.Model);

            // Add the new one
            var center = new Point3D(currentMazeData.Spawn[0], currentMazeData.Spawn[1], currentMazeData.Spawn[2]);
            player = new Sphere(center, ROUNDNESS, PLAYER_COLOR);
            movementVector = new Vector3D();
            modelGrp.Children.Add(player.Model);
        }

        /// <summary>
        /// Aplies a rotateTransform on the board depending on the TiltDir[]
        /// </summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            // Tilt
            var tiltVector = new Vector3D(0, 0, 0);
            if (tiltDirection[0]) tiltVector.X--;
            if (tiltDirection[1]) tiltVector.Y++;
            if (tiltDirection[2]) tiltVector.X++;
            if (tiltDirection[3]) tiltVector.Y--;
            modelGrp.Transform = new RotateTransform3D() { Rotation = new AxisAngleRotation3D(tiltVector, 20) };

            // Move the ball
            movementVector.X += (tiltVector.Y / 10);
            movementVector.Y -= (tiltVector.X / 10);
            Console.WriteLine(tiltVector.ToString());
            if (player != null) player.Model.Transform = new TranslateTransform3D(movementVector);
        }
        #endregion Private methods
    }
}
