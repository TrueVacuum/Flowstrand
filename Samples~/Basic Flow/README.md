# Basic Flow sample

`BasicFlow` demonstrates:

1. Entry and a starting Log.
2. Two parallel Delay branches.
3. Join All waiting for both branches.
4. A final Log after the join completes.

To run it:

1. Import this sample from Package Manager.
2. Add an empty GameObject to a scene.
3. Add `FlowGraphRunner`.
4. Assign `BasicFlow` and leave **Play On Start** enabled.
5. Open `BasicFlow`, enter Play mode, and select the Runner in **Debug Runner**.

The two delays run concurrently. Join All turns yellow after the first branch arrives and green
when the second branch arrives. The final Log then prints `Basic Flow completed`.
