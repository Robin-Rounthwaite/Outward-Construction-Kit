using System.Collections.Generic;
using System.Linq;
using NodeCanvas.DialogueTrees;
using NodeCanvas.Framework;
using UnityEngine;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Lets a placed character talk, using the game's own dialogue panel. A modder's lines become a dialogue tree, on the
    /// template the game keeps for its trainers, with its own conversation replaced.
    /// </summary>
    internal static class RegionDialogue
    {
        private const string TEMPLATE = "editor/templates/TrainerTemplate";

        /// <summary>Gives a character that's still asleep what it needs to talk, or says why it can't.</summary>
        public static void Attach(GameObject character, RegionManifest.TalkReference talk, RegionManifest manifest)
        {
            List<RegionManifest.DialogueLine> lines = manifest.dialogueLines.Where(l => l.dialogue == talk.dialogue).ToList();
            if (lines.Count == 0)
                throw new System.InvalidOperationException($"it has no lines in dialogue '{talk.dialogue}'");

            GameObject template = Resources.Load<GameObject>(TEMPLATE);
            if (template == null)
                throw new System.InvalidOperationException("the game's dialogue template is missing, perhaps after a game update");

            // Awake, not parented yet, because the character is asleep and its tree only exists once this has woken.
            GameObject holder = Object.Instantiate(template);
            holder.name = "OCK_Dialogue";

            // Its Trainer stays, because the talk prompt won't work without one. Nothing here ever asks it to train.
            DialogueActor actor = holder.GetComponentInChildren<DialogueActor>(true);
            actor.SetName(talk.name);
            var graph = (DialogueTree)holder.GetComponentInChildren<DialogueTreeController>(true).graph;
            graph.actorParameters[0].actor = actor;
            graph.actorParameters[0].name = actor.name;
            Build(graph, lines, actor.name);
            holder.transform.SetParent(character.transform, false);
        }

        private static void Build(DialogueTree graph, List<RegionManifest.DialogueLine> lines, string actorName)
        {
            graph.allNodes.Clear();
            var said = new Dictionary<string, StatementNodeExt>();
            foreach (RegionManifest.DialogueLine line in lines)
            {
                StatementNodeExt statement = graph.AddNode<StatementNodeExt>();
                statement.statement = new Statement(line.text);
                statement.SetActorName(actorName);
                said[line.node] = statement;
            }
            graph.primeNode = said[lines[0].node];

            foreach (RegionManifest.DialogueLine line in lines)
            {
                if (line.choices.Count == 0)
                {
                    graph.ConnectNodes(said[line.node], graph.AddNode<FinishNode>());
                    continue;
                }
                MultipleChoiceNodeExt choices = graph.AddNode<MultipleChoiceNodeExt>();
                graph.ConnectNodes(said[line.node], choices);
                foreach (RegionManifest.DialogueChoice choice in line.choices)
                {
                    choices.availableChoices.Add(new MultipleChoiceNodeExt.Choice(new Statement(choice.text)));
                    StatementNodeExt next;
                    Node target = said.TryGetValue(choice.next, out next) ? next : (Node)graph.AddNode<FinishNode>();
                    graph.ConnectNodes(choices, target);
                }
            }
        }
    }
}
