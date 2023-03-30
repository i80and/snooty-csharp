namespace tinydocutils;

public class Parser {
    // The reStructuredText parser.

    private IStateBuilder _initialState = BodyState.Builder.Instance;
    private Inliner _inliner = new Inliner();

    public Parser() {}

    public void Parse(string inputstring, Document document) {
        var statemachine = new RSTStateMachine(
            new StateConfiguration(
                RSTState.STATE_CLASSES, _initialState
            )
        );
        var inputlines = Util.String2Lines(
            inputstring,
            tab_width: document.Settings.tab_width,
            convert_whitespace: true
        );

        statemachine.RunRST(new StringList(inputlines, null), document, inliner: _inliner);
    }
}
