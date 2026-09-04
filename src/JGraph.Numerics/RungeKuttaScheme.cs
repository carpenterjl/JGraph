namespace JGraph.Numerics;

/// <summary>
/// One explicit Runge–Kutta pair and everything the driver needs to run it the way MATLAB runs it:
/// the tableau, the error weights, the dense output, and the handful of conventions that differ
/// from solver to solver and decide, one bit at a time, exactly which steps get taken.
/// </summary>
/// <remarks>
/// <para>
/// The four schemes are Bogacki–Shampine 2(3) (<c>ode23</c>), Dormand–Prince 5(4) (<c>ode45</c>),
/// and Verner's 7(8) and 8(9) pairs (<c>ode78</c>, <c>ode89</c>). The constants were read off
/// MATLAB's <c>ode23.m</c>, <c>ode45.m</c>, <c>ode78.m</c>, <c>ode89.m</c> and the
/// <c>ntrp23/45/78/89.m</c> interpolants beside them, and the two Verner pairs are kept as the
/// decimal literals those files carry rather than as fractions, because a fraction rounded here
/// and a literal rounded there are not always the same double.
/// </para>
/// <para>
/// The conventions matter more than they look. A fixture pins <c>nsteps</c> exact, and a step is
/// accepted or refused on the last bit of an error estimate: whether the solution is formed with
/// the step length before or after it is purified against the clock, whether the weights are
/// scaled by the step before or after the stages are summed, and what a retried step measures its
/// error against, each move that bit. Each scheme records what its reference does.
/// </para>
/// </remarks>
public sealed class RungeKuttaScheme
{
    /// <summary>MATLAB's name for the solver.</summary>
    public required string Name { get; init; }

    /// <summary>The nodes, one per stage of an attempt; the first is 0.</summary>
    public required double[] C { get; init; }

    /// <summary>The tableau rows: stage s combines the stages before it with <c>A[s]</c>.</summary>
    public required double[][] A { get; init; }

    /// <summary>The weights that form the step's solution.</summary>
    public required double[] B { get; init; }

    /// <summary>The error weights: the higher-order solution less the lower-order one.</summary>
    public required double[] E { get; init; }

    /// <summary>One over the order the step control works at: 1/3, 1/5, 1/8, 1/9.</summary>
    public required double ErrorExponent { get; init; }

    /// <summary>The least a first refusal shrinks the step by: 0.5 for <c>ode23</c>, 0.1 elsewhere.</summary>
    public required double ShrinkFloor { get; init; }

    /// <summary>Points reported per accepted step when nobody says otherwise.</summary>
    public required int DefaultRefine { get; init; }

    /// <summary>
    /// Whether the last stage of a step is the first of the next. The pairs that have this
    /// evaluate their last stage at the step's own solution, after the solution is formed.
    /// </summary>
    public required bool FirstSameAsLast { get; init; }

    /// <summary>
    /// Whether a stage is <c>y + Σ f·(h·a)</c> rather than <c>y + h·Σ a·f</c>. <c>ode23</c> forms
    /// <c>h·B</c> once and multiplies the stages into it; every other solver sums first.
    /// </summary>
    public bool WeightsScaledByStep { get; init; }

    /// <summary>
    /// Whether the step length is purified — replaced by <c>(t + h) − t</c> — before the solution
    /// is formed with it. <c>ode45</c> does; <c>ode23</c>, <c>ode78</c> and <c>ode89</c> form the
    /// solution first and purify afterwards.
    /// </summary>
    public bool PurifyBeforeSolution { get; init; }

    /// <summary>
    /// Whether a retried step measures its error against the state it started from alone, rather
    /// than against the larger of that and the new state. The Verner pairs do.
    /// </summary>
    public bool RetryWeightIgnoresNewState { get; init; }

    /// <summary>
    /// Whether an error that is not a number refuses the step. The Verner pairs test
    /// <c>~(err &lt;= rtol)</c>, which a NaN fails; the older pairs test <c>err &gt; rtol</c>, which
    /// it passes.
    /// </summary>
    public bool NanErrorFails { get; init; }

    /// <summary>
    /// Which stages the interpolant reads, as indices into the full stage list — the attempt's
    /// stages first, then the continuation stages. These are also the columns of the solution
    /// structure's <c>idata.f3d</c>, in this order.
    /// </summary>
    public required int[] InterpolationStages { get; init; }

    /// <summary>The node each interpolation stage was evaluated at, as a fraction of the step.</summary>
    public required double[] InterpolationNodes { get; init; }

    /// <summary>
    /// The dense output: for interpolation stage j, <c>Dense[j][p]</c> is the coefficient of
    /// θ^(p+1), so the state at fraction θ through a step is <c>y + h·Σⱼ fⱼ·Σₚ Dense[j][p]·θ^(p+1)</c>.
    /// </summary>
    public required double[][] Dense { get; init; }

    /// <summary>
    /// The nodes of the stages the interpolant needs beyond the attempt's own, evaluated only
    /// when something asks to read inside a step. Empty for the pairs whose stages suffice.
    /// </summary>
    public double[] ContinuationNodes { get; init; } = [];

    /// <summary>
    /// The weights forming each continuation stage, indexed over the full stage list — the
    /// attempt's stages and the continuation stages before it.
    /// </summary>
    public double[][] ContinuationWeights { get; init; } = [];

    /// <summary>How many stages an attempt evaluates, the first included.</summary>
    public int AttemptStages => C.Length;

    /// <summary>How many stages there are in all, continuation stages included.</summary>
    public int AllStages => C.Length + ContinuationNodes.Length;

    /// <summary>The interpolation stages picked out of the full stage list, in the interpolant's order.</summary>
    public double[][] InterpolationStagesOf(double[][] stages)
    {
        var picked = new double[InterpolationStages.Length][];
        for (int j = 0; j < picked.Length; j++)
        {
            picked[j] = stages[InterpolationStages[j]];
        }

        return picked;
    }

    /// <summary>
    /// The state at <paramref name="at"/> inside the step from <paramref name="t"/> of length
    /// <paramref name="h"/>, off the dense output over the interpolation stages alone (in the
    /// order of <see cref="InterpolationStages"/>, which is the order the solution structure
    /// stores them in); and, when <paramref name="slope"/> is given, the derivative there as well.
    /// </summary>
    /// <remarks>
    /// The polynomial is summed stage by stage and power by power in the same order the ode45
    /// interpolant always used here, so nothing that read a solution before this file existed
    /// reads a different number now.
    /// </remarks>
    public double[] Interpolate(double t, double h, double[] y, double[][] stages, double at,
        double[]? slope, int[]? nonNegative)
    {
        double s = (at - t) / h;
        int n = y.Length;
        var value = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            double rate = 0;
            for (int j = 0; j < InterpolationStages.Length; j++)
            {
                double[] row = Dense[j];
                double stage = stages[j][i];
                double power = s;   // s^(p+1), the weight's own term
                double lower = 1;   // s^p, one power down, which is what the derivative takes
                double weight = 0;
                double gradient = 0;
                for (int p = 0; p < row.Length; p++)
                {
                    weight += row[p] * power;
                    gradient += row[p] * (p + 1) * lower;
                    lower = power;
                    power *= s;
                }

                sum += weight * stage;
                rate += gradient * stage;
            }

            value[i] = y[i] + (h * sum);
            if (slope is not null)
            {
                slope[i] = rate;
            }
        }

        if (nonNegative is not null)
        {
            // A component held non-negative is read as non-negative too, and its slope with it.
            foreach (int index in nonNegative)
            {
                if (value[index] < 0)
                {
                    value[index] = 0;
                    if (slope is not null)
                    {
                        slope[index] = 0;
                    }
                }
            }
        }

        return value;
    }

    /// <summary>Bogacki–Shampine 2(3): MATLAB's <c>ode23</c>.</summary>
    public static RungeKuttaScheme BogackiShampine { get; } = new()
    {
        Name = "ode23",
        C = [0, 1.0 / 2, 3.0 / 4, 1],
        A =
        [
            [],
            [1.0 / 2],
            [0, 3.0 / 4],
            [2.0 / 9, 1.0 / 3, 4.0 / 9],
        ],
        B = [2.0 / 9, 1.0 / 3, 4.0 / 9, 0],
        E = [-5.0 / 72, 1.0 / 12, 1.0 / 9, -1.0 / 8],
        ErrorExponent = 1.0 / 3,
        ShrinkFloor = 0.5,
        DefaultRefine = 1,
        FirstSameAsLast = true,
        WeightsScaledByStep = true,
        NanErrorFails = false,
        InterpolationStages = [0, 1, 2, 3],
        InterpolationNodes = [0, 1.0 / 2, 3.0 / 4, 1],
        Dense =
        [
            [1, -4.0 / 3, 5.0 / 9],
            [0, 1, -2.0 / 3],
            [0, 4.0 / 3, -8.0 / 9],
            [0, -1, 1],
        ],
    };

    /// <summary>Dormand–Prince 5(4): MATLAB's <c>ode45</c>.</summary>
    public static RungeKuttaScheme DormandPrince { get; } = new()
    {
        Name = "ode45",
        C = [0, 1.0 / 5, 3.0 / 10, 4.0 / 5, 8.0 / 9, 1, 1],
        A =
        [
            [],
            [1.0 / 5],
            [3.0 / 40, 9.0 / 40],
            [44.0 / 45, -56.0 / 15, 32.0 / 9],
            [19372.0 / 6561, -25360.0 / 2187, 64448.0 / 6561, -212.0 / 729],
            [9017.0 / 3168, -355.0 / 33, 46732.0 / 5247, 49.0 / 176, -5103.0 / 18656],
            [35.0 / 384, 0, 500.0 / 1113, 125.0 / 192, -2187.0 / 6784, 11.0 / 84],
        ],
        B = [35.0 / 384, 0, 500.0 / 1113, 125.0 / 192, -2187.0 / 6784, 11.0 / 84, 0],
        E = [71.0 / 57600, 0, -71.0 / 16695, 71.0 / 1920, -17253.0 / 339200, 22.0 / 525, -1.0 / 40],
        ErrorExponent = 1.0 / 5,
        ShrinkFloor = 0.1,
        DefaultRefine = 4,
        FirstSameAsLast = true,
        PurifyBeforeSolution = true,
        NanErrorFails = false,
        InterpolationStages = [0, 1, 2, 3, 4, 5, 6],
        InterpolationNodes = [0, 1.0 / 5, 3.0 / 10, 4.0 / 5, 8.0 / 9, 1, 1],
        Dense =
        [
            [1, -183.0 / 64, 37.0 / 12, -145.0 / 128],
            [0, 0, 0, 0],
            [0, 1500.0 / 371, -1000.0 / 159, 1000.0 / 371],
            [0, -125.0 / 32, 125.0 / 12, -375.0 / 64],
            [0, 9477.0 / 3392, -729.0 / 106, 25515.0 / 6784],
            [0, -11.0 / 7, 11.0 / 3, -55.0 / 28],
            [0, 3.0 / 2, -4, 5.0 / 2],
        ],
    };

    /// <summary>Verner's 7(8) pair with its 7th-order continuous extension: MATLAB's <c>ode78</c>.</summary>
    public static RungeKuttaScheme Verner78 { get; } = BuildVerner78();

    /// <summary>Verner's 8(9) pair with its 8th-order continuous extension: MATLAB's <c>ode89</c>.</summary>
    public static RungeKuttaScheme Verner89 { get; } = BuildVerner89();

    /// <summary>The scheme MATLAB's solver of that name runs, or null for a name that is not one of the four.</summary>
    public static RungeKuttaScheme? Named(string solver) => solver switch
    {
        "ode23" => BogackiShampine,
        "ode45" => DormandPrince,
        "ode78" => Verner78,
        "ode89" => Verner89,
        _ => null,
    };

    /// <summary>
    /// A tableau row written the way the reference writes it — <c>(stage, weight)</c> pairs over
    /// stages numbered from one — so the transcription reads like the file it came from.
    /// </summary>
    private static double[] Row(int length, params (int Stage, double Weight)[] terms)
    {
        var row = new double[length];
        foreach ((int stage, double weight) in terms)
        {
            row[stage - 1] = weight;
        }

        return row;
    }

    /// <summary>Dense-output rows from the interpolant's per-power vectors: stage j gets θ^(p+1)·columns[p][j], and stage 1 gets θ itself as well.</summary>
    private static double[][] DenseRows(int stages, params double[][] columns)
    {
        var rows = new double[stages][];
        for (int j = 0; j < stages; j++)
        {
            rows[j] = new double[columns.Length + 1];
            rows[j][0] = j == 0 ? 1 : 0;
            for (int p = 0; p < columns.Length; p++)
            {
                rows[j][p + 1] = columns[p][j];
            }
        }

        return rows;
    }

    private static RungeKuttaScheme BuildVerner78()
    {
        const int stages = 13;
        double[][] a =
        [
            Row(stages),
            Row(stages, (1, 0.05)),
            Row(stages, (1, -0.0069931640625), (2, 0.1135556640625)),
            Row(stages, (1, 0.0399609375), (3, 0.1198828125)),
            Row(stages, (1, 0.36139756280045754), (3, -1.3415240667004928), (4, 1.3701265039000352)),
            Row(stages, (1, 0.049047202797202795), (4, 0.23509720422144048), (5, 0.18085559298135673)),
            Row(stages, (1, 0.06169289044289044), (4, 0.11236568314640277), (5, -0.03885046071451367),
                (6, 0.01979188712522046)),
            Row(stages, (1, -1.767630240222327), (4, -62.5), (5, -6.061889377376669), (6, 5.6508231982227635),
                (7, 65.62169641937624)),
            Row(stages, (1, -1.1809450665549708), (4, -41.50473441114321), (5, -4.434438319103725),
                (6, 4.260408188586133), (7, 43.75364022446172), (8, 0.00787142548991231)),
            Row(stages, (1, -1.2814059994414884), (4, -45.047139960139866), (5, -4.731362069449577),
                (6, 4.514967016593808), (7, 47.44909557172985), (8, 0.010592282971116612),
                (9, -0.0057468422638446166)),
            Row(stages, (1, -1.7244701342624853), (4, -60.92349008483054), (5, -5.951518376222393),
                (6, 5.556523730698456), (7, 63.98301198033305), (8, 0.014642028250414961),
                (9, 0.06460408772358203), (10, -0.0793032316900888)),
            Row(stages, (1, -3.301622667747079), (4, -118.01127235975251), (5, -10.141422388456112),
                (6, 9.139311332232058), (7, 123.37594282840426), (8, 4.62324437887458),
                (9, -3.3832777380682018), (10, 4.527592100324618), (11, -5.828495485811623)),
            Row(stages, (1, -3.039515033766309), (4, -109.26086808941763), (5, -9.290642497400293),
                (6, 8.43050498176491), (7, 114.20100103783314), (8, -0.9637271342145479),
                (9, -5.0348840888021895), (10, 5.958130824002923)),
        ];

        double[] b = Row(stages, (1, 0.04427989419007951), (6, 0.3541049391724449), (7, 0.2479692154956438),
            (8, -15.694202038838084), (9, 25.084064965558564), (10, -31.738367786260277),
            (11, 22.938283273988784), (12, -0.2361324633071542));

        double[] e = Row(stages, (1, 3.272103901028776e-05), (6, 0.0005046250618777735),
            (7, -0.00012117235897844563), (8, 20.142336771313868), (9, -5.237178599439828),
            (10, 8.156744408794658), (11, -22.938283273988784), (12, 0.2361324633071542),
            (13, -0.36016794372897754));

        // The four stages the interpolant needs beyond the attempt's, numbered 14 to 17 as the
        // reference numbers them; each may use the ones before it.
        const int all = stages + 4;
        double[][] continuation =
        [
            Row(all, (1, 0.04427989419007951), (6, 0.3541049391724449), (7, 0.2479692154956438),
                (8, -15.694202038838084), (9, 25.084064965558564), (10, -31.738367786260277),
                (11, 22.938283273988784), (12, -0.2361324633071542)),
            Row(all, (1, 0.04620700646754963), (6, 0.045039041608424805), (7, 0.23368166977134244),
                (8, 37.83901368421068), (9, -15.949113289454246), (10, 23.028368351816102),
                (11, -44.85578507769412), (12, -0.06379858768647444), (14, -0.012595035543861663)),
            Row(all, (1, 0.05037946855482041), (6, 0.041098361310460796), (7, 0.17180541533481958),
                (8, 4.614105319981519), (9, -1.7916678830853965), (10, 2.531658930485041),
                (11, -5.324977860205731), (12, -0.03065532595385635), (14, -0.005254479979429613),
                (15, -0.08399194644224793)),
            Row(all, (1, 0.0408289713299708), (6, 0.4244479514247632), (7, 0.23260915312752345),
                (8, 2.677982520711806), (9, 0.7420826657338945), (10, 0.1460377847941461),
                (11, -3.579344509890565), (12, 0.11388443896001738), (14, 0.012677906510331901),
                (15, -0.07443436349946675), (16, 0.047827480797578516)),
        ];

        double[] bi2 =
        [
            -7.238550783576432811855355839508646327161, 11.15330887588935170976376962782446833855,
            2.34875229807309355640904629061136935335, -1027.321675339240679090464776362465090654,
            1568.546608927281956416687915664731868885, -2000.882061921041961546811133479107090218,
            1496.620400693446268810344884971434468267, -16.41320775560933621675902845723196069900,
            -4.29672443178246482824254064733546854251, -20.41628069294821485579834313809132051248,
            16.53007184264271512356106095760699278945, -18.63064171313429626683549958846959067803,
        ];
        double[] bi3 =
        [
            26.00913483254676138219215542805486438340, -91.7609656398961659890179437322816238711,
            -11.6724894172018429369093778842231443146, 9198.71432360760879019681406218311101879,
            -13995.38852541600542155322174511897930298, 17864.36380347691630038038755096765127729,
            -13397.55405171476021512904990709508924800, 147.6097045407002371315249807692915435608,
            38.6444746111678092366406218271498656093, 153.5213232524836445391962375168798263930,
            -96.6861433615782065041742809436987893361, 164.1994112280183092456176460821337125030,
        ];
        double[] bi4 =
        [
            -50.23684777762566731759165474184543812128, 291.7074241722059450113911477530513089255,
            -3.339139076505928386509206543237093540, -33189.78048157363822223641020734287802492,
            50256.2124698102445419491620666726469821, -64205.1907515562863000297926577113695108,
            48323.5602199437493999696912750109765015, -535.719963714732106447158760197417632645,
            -140.3503471762808981414524290552248895548, -436.5502610211220460266289847121377276100,
            268.959934219531723149495873437076657635, -579.272256249540441494196462569641132906,
        ];
        double[] bi5 =
        [
            52.12072084601022449485077581012685809554, -430.4096692910862817449451677633631387823,
            94.885262249720610030798242337479596095, 57750.0831348887181073584126028277545727,
            -86974.5128036219909523950692144595063700, 111224.8489930378077126420609392735999202,
            -84051.4283423393032636942266780744607468, 938.286247077820650371318861625025573381,
            246.3954669697502467443139611011701827640, 598.214644262650861959065070073603792110,
            -428.681909788964647271837835032326719249, 980.198255708866731505258442280896479501,
        ];
        double[] bi6 =
        [
            -27.06472451211777193118825764262673140465, 299.4531188198997479843407054776900024282,
            -143.071126583012024456409244370652716962, -47698.93315706261990169947144294597707756,
            71494.7977095997701213661747332399327008, -91509.3392102130338542605593697286718077,
            69399.8582111570893316100585838633124312, -779.438309639349328345148153897689081893,
            -205.8341686964167118696204191085878165880, -398.7823950071290897160364203878571043995,
            354.578231152433375494079868740183658991, -786.224179015513894176220583239056456901,
        ];
        double[] bi7 =
        [
            5.454547288952965694339504452480078562780, -79.78911199784015209705095616004766020335,
            61.0967097444217359754873031115590556707, 14951.54365344033382142012769129774268946,
            -22324.57139433374168317029445568645401598, 28594.46085938937782634638310955782423389,
            -21748.11815446623273761450332307272543593, 245.4393970278627292916961100938952065362,
            65.44129872356201885836080588282812631205, 104.0129692060648441002024406476025340187,
            -114.7001840640649599911246871588418008302, 239.7294100413035911863764570341369884827,
        ];

        return new RungeKuttaScheme
        {
            Name = "ode78",
            C = [0, 0.05, 0.1065625, 0.15984375, 0.39, 0.465, 0.155, 0.943, 0.901802041735857, 0.909, 0.94, 1, 1],
            A = a,
            B = b,
            E = e,
            ErrorExponent = 1.0 / 8,
            ShrinkFloor = 0.1,
            DefaultRefine = 8,
            FirstSameAsLast = false,
            RetryWeightIgnoresNewState = true,
            NanErrorFails = true,
            ContinuationNodes = [1, 0.3110177634953864, 0.1725, 0.7846],
            ContinuationWeights = continuation,

            // f1, f6 to f12, then the four continuation stages f14 to f17.
            InterpolationStages = [0, 5, 6, 7, 8, 9, 10, 11, 13, 14, 15, 16],
            InterpolationNodes = [0, 0.465, 0.155, 0.943, 0.901802041735857, 0.909, 0.94, 1, 1, 0.3110177634953864, 0.1725, 0.7846],
            Dense = DenseRows(12, bi2, bi3, bi4, bi5, bi6, bi7),
        };
    }

    private static RungeKuttaScheme BuildVerner89()
    {
        const int stages = 16;
        double[][] a =
        [
            Row(stages),
            Row(stages, (1, 0.04)),
            Row(stages, (1, -0.01988527319182291), (2, 0.11637263332969652)),
            Row(stages, (1, 0.0361827600517026), (3, 0.10854828015510781)),
            Row(stages, (1, 2.2721142642901775), (3, -8.526886447976398), (4, 6.830772183686221)),
            Row(stages, (1, 0.050943855353893744), (4, 0.1755865049809071), (5, 0.0007022961270757468)),
            Row(stages, (1, 0.1424783668683285), (4, -0.35417994346686843), (5, 0.07595315450295101),
                (6, 0.6765157656337123)),
            Row(stages, (1, 0.07111111111111111), (6, 0.32799092876058983), (7, 0.24089796012829906)),
            Row(stages, (1, 0.07125), (6, 0.32688424515752457), (7, 0.11561575484247544), (8, -0.03375)),
            Row(stages, (1, 0.048226773224658105), (6, 0.039485599804954), (7, 0.10588511619346581),
                (8, -0.021520063204743093), (9, -0.10453742601833482)),
            Row(stages, (1, -0.026091134357549235), (6, 0.03333333333333333), (7, -0.1652504006638105),
                (8, 0.03434664118368617), (9, 0.1595758283215209), (10, 0.21408573218281934)),
            Row(stages, (1, -0.03628423396255659), (6, -1.0961675974272087), (7, 0.1826035504321331),
                (8, 0.07082254444170684), (9, -0.02313647018482431), (10, 0.27112047263209327),
                (11, 1.3081337494229808)),
            Row(stages, (1, -0.5074635056416975), (6, -6.631342198657237), (7, -0.2527480100908801),
                (8, -0.49526123800360955), (9, 0.2932525545253887), (10, 1.440108693768281),
                (11, 6.237934498647056), (12, 0.7270192054526987)),
            Row(stages, (1, 0.6130118256955932), (6, 9.088803891640463), (7, -0.40737881562934486),
                (8, 1.7907333894903747), (9, 0.714927166761755), (10, -1.438580857841723),
                (11, -8.26332931206474), (12, -1.5375705708088652), (13, 0.34538328275648716)),
            Row(stages, (1, -1.2116979103438739), (6, -19.055818715595954), (7, 1.2630606753898752),
                (8, -6.913916969178458), (9, -0.676462266509498), (10, 3.367860445026608),
                (11, 18.00675164312591), (12, 6.83882892679428), (13, -1.0315164519219504),
                (14, 0.41291062321306227)),
            Row(stages, (1, 2.1573890074940536), (6, 23.807122198095804), (7, 0.8862779249216556),
                (8, 13.139130397598764), (9, -2.6044157092877147), (10, -5.193859949783873),
                (11, -20.412340711541507), (12, -12.300856252505723), (13, 1.5215530950085394)),
        ];

        double[] b = Row(stages, (1, 0.014588852784055396), (8, 0.0020241978878893325), (9, 0.21780470845697167),
            (10, 0.12748953408543898), (11, 0.2244617745463132), (12, 0.1787254491259903),
            (13, 0.07594344758096558), (14, 0.12948458791975614), (15, 0.029477447612619417));

        double[] e = Row(stages, (1, 0.005757813768188949), (8, 1.0675934530948108), (9, -0.14099636134393978),
            (10, -0.014411715396914925), (11, 0.030796961251883033), (12, -1.1613152578179067),
            (13, 0.32221113486118586), (14, -0.12948458791975614), (15, -0.029477447612619417),
            (16, 0.04932600711506839));

        // The five continuation stages, numbered 17 to 21 as the reference numbers them.
        const int all = stages + 5;
        double[][] continuation =
        [
            Row(all, (1, 0.014588852784055396), (8, 0.0020241978878893325), (9, 0.21780470845697167),
                (10, 0.12748953408543898), (11, 0.2244617745463132), (12, 0.1787254491259903),
                (13, 0.07594344758096558), (14, 0.12948458791975614), (15, 0.029477447612619417)),
            Row(all, (1, 0.015601405261088616), (8, 0.26811643933275847), (9, 0.1883053124587791),
                (10, 0.12491991374610308), (11, 0.2302302127814522), (12, -0.13603122161327985),
                (13, 0.07488659971306953), (14, -0.02812840029795629), (15, -0.023144557264819496),
                (17, 0.027345304241113474)),
            Row(all, (1, 0.013111957218440684), (8, -0.1464024265969827), (9, 0.2471264389666796),
                (10, 0.13113752030800324), (11, 0.21705603469825827), (12, 0.286753671376032),
                (13, 0.02323311339149422), (14, 0.05250677264199396), (15, 0.0028339515860099506),
                (17, -0.008502403851995712), (18, 0.06914537026206649)),
            Row(all, (1, 0.013989212133617684), (8, -0.031574065179505), (9, 0.2271812513272158),
                (10, 0.12894864109967866), (11, 0.2216682589135277), (12, 0.19483682365424806),
                (13, 0.05740088404417653), (14, 0.09008366542675955), (15, 0.015791532088442122),
                (17, -0.018991315059091858), (18, -0.08830926811918835), (19, -0.11502562032988092)),
            Row(all, (1, 0.016151472919007624), (8, 0.08098685003242906), (9, 0.12769162943069304),
                (10, 0.12348143593834805), (11, 0.233985125914011), (12, -0.06595995683357368),
                (13, -0.02565276859406433), (14, -0.1258973463819247), (15, -0.04307672490364844),
                (17, 0.04973042479196705), (18, 0.10004735401793927), (19, 0.13786588067636232),
                (20, -0.12235337700754625)),
        ];

        double[] bi2 =
        [
            -12.75304069282388950483064356409920964903, -.7205785602508598770412906345635211707530,
            -48.06969107148755163304089843112677204750, 16.32345788425353372538518290168630386345,
            -5.888504109270884968456670963647316074790, -69.22821100686856642029708151339949374410,
            -38.04668072585188932845088326881300565231, -75.21598899610186748511604166683735788867,
            -19.46588639117053206710108537195779499764, 22.25276964616901764060657108171977843291,
            14.38227638804283974976194859491865842966, 94.92756297288050252130347529152607755873,
            63.97757128518312942674385099931772857860, 57.52494337729701822053356654527592436144,
        ];
        double[] bi3 =
        [
            68.54470113831162103032818060021729044674, 6.559119452090996226782640921071801708575,
            451.8280048138745279924509263176669733181, -118.7000544943430560099961188668917723161,
            89.44704113715942232261735606938508401319, 627.4402883568152894700875088172124339413,
            340.9894379782233715580222226199659613703, 670.5551756563966247587443735545005782764,
            172.8442419404515527792492527636420780662, -202.2239493340537483960420170974810960961,
            -301.8862921284913174289877784920239768093, -819.3810521264963242304829331408227079441,
            -398.4710369466142415586467341825347335632, -587.5456254433247185141268798839079144120,
        ];
        double[] bi4 =
        [
            -194.8086610529652454702496599846926584629, -23.65417183348355244612060745867113623926,
            -1652.497181212881040972312045847339995538, 379.7566858308294928207030358265089540700,
            -380.5212519133325379643523316755978956588, -2258.351433966898561277167615407280413114,
            -1221.089637320158271327850635113537223709, -2395.425253052829223230171636642290078449,
            -616.3040486605512589130986770456245791880, 741.1380586926791133828821084525361362850,
            1781.158815560130838385479471257488378388, 2811.287710727701073955763185548552769132,
            716.5966869905804904541294272473572154569, 2312.713681211178682602365980842590527027,
        ];
        double[] bi5 =
        [
            317.7392440058917273091720330437842255568, 44.16895609281556502164197054695369477469,
            3109.740640759030910750811309301753788556, -658.7110535057816543655898314260197627370,
            770.9937272473377188110779497353201847420, 4212.395800215491820633428188331388768232,
            2271.121533555259071203608357605468537656, 4449.143231627101738352915330060972953361,
            1143.483637444009748019174885933341490497, -1419.345991543901288934527221623894382397,
            -4706.785555404445608825447884178551282728, -4973.994583541372152306531194715881127387,
            52.89052166461839778508215175982407959882, -4612.840108616055993454816044374461167725,
        ];
        double[] bi6 =
        [
            -299.5110317016553611442365899609354277447, -45.39036357573745767095716350329312304563,
            -3211.322751886536222454319056864879758831, 644.2805206048469902599556429935393520064,
            -831.4716893548379599332950159082545018560, -4325.916149192490627782429268979382924699,
            -2328.099120585713191256845645730711618239, -4556.769050179329341174034103718438911683,
            -1170.357669638643781845251579162353094100, 1505.723453115174938364140876028053730124,
            6291.645831877559252216657958850293660213, 4810.867980444981597632278117658725359609,
            -1470.500844962700814722191656818947551329, 4986.820885035081979510527485116584809575,
        ];
        double[] bi7 =
        [
            151.9504248851541972968268022793780785647, 24.43461389111572285100991773589739045465,
            1734.432866374712319698880133120955280438, -334.8751825767893992539894957157903467478,
            461.9387177871916189774255799856083869291, 2327.648261456242912420669452255092833474,
            1251.132631830924456968695609885535799254, 2447.365350086656490540478347199404246857,
            628.2905498131297391288540900112436704439, -841.3580375761078273196578619974820483141,
            -4164.432457468105482852522949304279052327, -2417.563170381452428143421579409061339218,
            1524.433133991196905505389714285761298379, -2793.397702113869225818637760332264198189,
        ];
        double[] bi8 =
        [
            -32.14704772912899411981910701782974118232, -5.395551268662524772664900940711910826177,
            -383.8940830682559717177907419834455933213, 72.05311579106953179281845208383275272780,
            -104.2735790197010640588910140881002072916, -513.8098304131662767347811744202515986459,
            -275.9322212851025822454499429637433189491, -539.5239805539746656111293686438409662456,
            -138.4613470596128476846868080169758635556, 193.8136970000397952625975451565478819654,
            1085.917381175309478755059233272153614833, 493.8555519037577305710909287669609682492,
            -488.9260320222638668905067532907780371207, 636.7239265496922574541536520861820193631,
        ];

        return new RungeKuttaScheme
        {
            Name = "ode89",
            C =
            [
                0, 0.04, 0.09648736013787361, 0.1447310402068104, 0.576, 0.2272326564618766,
                0.5407673435381234, 0.64, 0.48, 0.06754, 0.25, 0.6770920153543243, 0.8115, 0.906, 1, 1,
            ],
            A = a,
            B = b,
            E = e,
            ErrorExponent = 1.0 / 9,
            ShrinkFloor = 0.1,
            DefaultRefine = 8,
            FirstSameAsLast = false,
            RetryWeightIgnoresNewState = true,
            NanErrorFails = true,
            ContinuationNodes = [1, 0.7421010083583088, 0.888, 0.696, 0.487],
            ContinuationWeights = continuation,

            // f1, f8 to f15, then the five continuation stages f17 to f21.
            InterpolationStages = [0, 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20],
            InterpolationNodes =
            [
                0, 0.64, 0.48, 0.06754, 0.25, 0.6770920153543243, 0.8115, 0.906, 1, 1,
                0.7421010083583088, 0.888, 0.696, 0.487,
            ],
            Dense = DenseRows(14, bi2, bi3, bi4, bi5, bi6, bi7, bi8),
        };
    }
}
