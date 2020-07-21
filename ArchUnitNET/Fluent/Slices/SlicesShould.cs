﻿//  Copyright 2019 Florian Gather <florian.gather@tngtech.com>
// 	Copyright 2019 Paula Ruiz <paularuiz22@gmail.com>
// 	Copyright 2019 Fritz Brandhuber <fritz.brandhuber@tngtech.com>
// 
// 	SPDX-License-Identifier: Apache-2.0
// 

using System.Collections.Generic;
using System.Linq;
using ArchUnitNET.Domain;
using StronglyConnectedComponents;

namespace ArchUnitNET.Fluent.Slices
{
    public class SlicesShould
    {
        private readonly SliceRuleCreator _ruleCreator;

        public SlicesShould(SliceRuleCreator ruleCreator)
        {
            _ruleCreator = ruleCreator;
        }

        public SliceRule BeFreeOfCycles()
        {
            IEnumerable<EvaluationResult> Evaluate(IEnumerable<Slice> slices, ICanBeEvaluated archRule,
                Architecture architecture)
            {
                var slicesList = slices.ToList();

                IEnumerable<Slice> FindDependencies(Slice slice)
                {
                    var typeDependencies = slice.Dependencies.Select(dependency => dependency.Target).Distinct();
                    return typeDependencies.Select(type => slicesList.FirstOrDefault(slc => slc.Types.Contains(type)))
                        .Where(slc => slc != null);
                }

                var cycles = slicesList.DetectCycles(FindDependencies)
                    .Where(dependencyCycle => dependencyCycle.IsCyclic).ToList();

                if (cycles.Any())
                {
                    foreach (var cycle in cycles)
                    {
                        var description = "Cycle found:";
                        foreach (var slice in cycle.Contents)
                        {
                            var dependencies = slice.Dependencies.ToList();
                            foreach (var otherSlice in cycle.Contents.Except(new[] {slice}))
                            {
                                var depsToSlice = dependencies.Where(dependency =>
                                    otherSlice.Types.Contains(dependency.Target)).ToList();
                                if (depsToSlice.Any())
                                {
                                    description += "\n" + slice.Description + " -> " + otherSlice.Description;
                                    description = depsToSlice.Aggregate(description,
                                        (current, dependency) =>
                                            current + ("\n\t" + dependency.Origin + " -> " + dependency.Target));
                                }
                            }
                        }

                        yield return new EvaluationResult(cycle, false, description, archRule, architecture);
                    }
                }
                else
                {
                    yield return new EvaluationResult(slicesList, true, "All Slices are free of cycles.", archRule,
                        architecture);
                }
            }

            _ruleCreator.SetEvaluationFunction(Evaluate);
            _ruleCreator.AddToDescription("be free of cycles");
            return new SliceRule(_ruleCreator);
        }

        public SliceRule NotDependOnEachOther()
        {
            IEnumerable<EvaluationResult> Evaluate(IEnumerable<Slice> slices, ICanBeEvaluated archRule,
                Architecture architecture)
            {
                var slicesList = slices.ToList();

                IEnumerable<Slice> FindDependencies(Slice slice)
                {
                    var typeDependencies = slice.Dependencies.Select(dependency => dependency.Target).Distinct();
                    return typeDependencies.Select(type => slicesList.FirstOrDefault(slc => slc.Types.Contains(type)))
                        .Where(slc => slc != null);
                }

                foreach (var slice in slicesList)
                {
                    var sliceDependencies = FindDependencies(slice).ToList();
                    var passed = !sliceDependencies.Any();
                    var description = slice.Description + " does not depend on another slice.";
                    if (!passed)
                    {
                        description = slice.Description + " does depend on other slices:";
                        foreach (var sliceDependency in sliceDependencies)
                        {
                            var depsToSlice = slice.Dependencies.Where(dependency =>
                                sliceDependency.Types.Contains(dependency.Target)).ToList();
                            description += "\n" + slice.Description + " -> " + sliceDependency.Description;
                            description = depsToSlice.Aggregate(description,
                                (current, dependency) =>
                                    current + ("\n\t" + dependency.Origin + " -> " + dependency.Target));
                        }
                    }

                    yield return new EvaluationResult(slice, passed, description, archRule, architecture);
                }
            }

            _ruleCreator.SetEvaluationFunction(Evaluate);
            _ruleCreator.AddToDescription("not depend on each other");
            return new SliceRule(_ruleCreator);
        }
    }
}