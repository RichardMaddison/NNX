﻿using System;
using System.Collections.Generic;
using NeuralNetworks.Utils;

namespace NeuralNetworks.Training
{
    public class Trainer : ITrainer
    {
        public TrainerConfig Config { get; set; }

        public Trainer() { }

        public Trainer(TrainerConfig config)
        {
            Config = config;
        }

        public INeuralNetwork Train(IList<InputOutput> trainingSet, INeuralNetwork nn)
        {
            if (Config == null)
                throw new NeuralNetworkException("Trainer is missing Config property.");

            var numEpochs = Config.NumEpochs;

            if (numEpochs <= 0)
                throw new NeuralNetworkException("Config.NumEpochs property should be a positive integer.  Was: " + Config.NumEpochs);

            if (nn == null)
                throw new ArgumentNullException(nameof(nn));

            var rand = RandomProvider.GetRandom(Config.Seed);

            if (Config.InitializeWeights)
                InitializeWeights(nn, rand);

            var prevWeightGradients = nn.Weights.DeepClone();

            foreach (var gradSet in prevWeightGradients)
            {
                for (var j = 0; j < gradSet.Length; j++)
                    gradSet[j] = 0;
            }

            for (var s = 0; s < numEpochs; s++)
            {
                var t = rand.Next(trainingSet.Count);
                var inputOutput = trainingSet[t];

                var gradients = nn.CalculateGradients(inputOutput.Input, inputOutput.Output);
                AdjustWeights(nn, gradients, prevWeightGradients);
                gradients.DeepCopyTo(prevWeightGradients);
            }

            return nn;
        }

        public static double GetError(INeuralNetwork nn, IList<InputOutput> testSet)
        {
            var error = 0.0;

            foreach (var inputOutput in testSet)
            {
                var result = nn.FeedForward(inputOutput.Input);
                error += ErrorCalculations.CrossEntropyError(inputOutput.Output, result.Output);
            }

            return error / testSet.Count;
        }

        public static double GetAccuracy(INeuralNetwork nn, IList<InputOutput> testSet)
        {
            var numHits = 0;

            foreach (var inputOutput in testSet)
            {
                var expected = inputOutput.Output.MaxIndex();
                var actual = nn.FeedForward(inputOutput.Input).Output.MaxIndex();

                numHits += expected == actual ? 1 : 0;
            }

            return ((double) numHits) / testSet.Count;
        }

        public void AdjustWeights(INeuralNetwork nn, double[][] weightGradients, double[][] prevWeightGradients)
        {
            var weights = nn.Weights;

            for (var i = 0; i < weightGradients.Length; i++)
            {
                var gradientSubList = weightGradients[i];
                var weightSubList = weights[i];

                for (var j = 0; j < gradientSubList.Length; j++)
                {
                    var prevWeight = weightSubList[j];
                    var fullGradient = gradientSubList[j] + Config.QuadraticRegularization * prevWeight +
                                        Config.Momentum * prevWeightGradients[i][j];
                    weightSubList[j] = prevWeight - Config.LearningRate * fullGradient;
                }
            }
        }

        public static void InitializeWeights(INeuralNetwork nn, IRandomGenerator rand)
        {
            var weights = nn.Weights;

            foreach (var weightsSubList in weights)
            {
                for (int i = 0; i < weightsSubList.Length; i++)
                    weightsSubList[i] = rand.NextDouble() * 0.2 - 0.1;
            }
        }

    }
}
