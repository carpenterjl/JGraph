# MATLAB Statistics and Machine Learning Toolbox coverage

**385 of 589 documented** Statistics and Machine Learning Toolbox names are implemented, as of
M53 wave J. Wave A built the scaffold — the list, this document and its verifier; wave B added the
descriptive and robust statistics; wave C the continuous distribution families; wave D the discrete
ones; wave E the distributions of a vector, and the samplers; wave F the hypothesis tests and the
analysis of variance; wave G the regressions; wave H the distances, the clusterings and the
multivariate analyses; wave I the distribution objects; wave J the designs of experiments, the
copulas, the remaining simulation names, the file readers and the plot verbs.

Nothing in the working set is pending: every name that is not implemented is excluded, by name, with
a reason.

## Where this list comes from

Like the Image Processing Toolbox, and unlike `matlab-builtin-coverage.md`, this one has no scraped
install behind it: the R2021b dump in the demo workspace came from a MATLAB with neither toolbox
installed, so `build-checklist.py` cannot see a single statistics name.

The list is therefore built once from MathWorks' archived R2021b reference — specifically the
alphabetical function list at `help/releases/R2021b/stats/functionlist-alpha.html`, read as markup
rather than retyped, so a mistranscribed or invented name is not a failure mode here. Reading the
release-pinned page is also what keeps anything introduced after R2021b out. The result is
`tools/matlab-checklist/matlab-r2021b-stats.csv`.

A hand-maintained doc over that list is still where counts rot, so
`tools/matlab-checklist/verify-stats-coverage.py` checks on demand that every listed name sits in
exactly one bucket below, that no bucket names a function that does not exist, that everything
called implemented is really registered in `JgsBuiltinCatalog.cs`, and that the headline count
matches the tables:

```bash
python tools/matlab-checklist/verify-stats-coverage.py
```

## Counting rule

**A name is counted when MathWorks gives it a reference page of its own.** The alphabetical list
holds 1,033 rows for 777 distinct names, because a name like `predict`, `anova` or `loss` gets one
page per class that owns it — `linearmodel.anova.html`, `repeatedmeasuresmodel.anova.html`, and so
on. Those 201 method-only names are not counted, because none of them exists apart from a class,
and every class that owns one is itself a row in the list. Counting them would inflate the
denominator with the same word a dozen times over.

Thirteen names are the deliberate exception. `cdf`, `icdf`, `pdf`, `random`, `truncate`,
`negloglik`, `paramci`, `proflik`, `iqr`, `mean`, `median`, `std` and `var` live under the `prob.`
package in the documentation, which by the rule above would file them as methods of
`NormalDistribution`. They are in fact the generic distribution interface — `cdf('Normal', x, 0, 1)`
needs no object at all — so they are counted, under `dist-objects`.

That leaves 589 documented names — 463 functions, 76 objects and 50 classes — each
appearing once, under its primary category. Unlike the IPT list, no whole family is collapsed
into a single row — the machine-read source made itemizing free, and the excluded families read
more honestly named one by one than hidden behind a count.

The four names in this list that JGraph's catalog already registers — `mean`, `median`, `std` and
`var` — are counted as not implemented, because what the toolbox documents under those names is the
statistic *of a probability distribution object*, which JGraph's base builtins do not take. `range`
is the sharper case and is recorded as a divergence below.

## Implemented — 385

### Descriptive and robust statistics, and the correlations — 31

Every one takes its documented option surface: a leading flag where the documentation puts one, then
a dimension, `'all'`, or nothing.

`corr`, `corrcov`, `crosstab`, `ecdf`, `ecdfhist`, `geomean`
`grpstats`, `harmmean`, `ksdensity`, `kurtosis`, `mad`, `moment`
`nancov`, `nanmax`, `nanmean`, `nanmedian`, `nanmin`, `nanstd`
`nansum`, `nanvar`, `nearcorr`, `partialcorr`, `partialcorri`, `prctile`
`quantile`, `range`, `skewness`, `tabulate`, `tiedrank`, `trimmean`
`zscore`

### Continuous distributions — 110

Seventeen families, each with its density, distribution function, quantile, random draw and moments,
plus the eleven fitters, the nine likelihoods, and the five generic names that take the distribution
as a word. Parameters MathWorks documents a default for may be left out and the rest are required by
name; the argument and the parameters expand against each other under the same singleton rule the
operators use; and the draws take sizes after every parameter, the way the base random constructors do.

`betacdf`, `betafit`, `betainv`, `betalike`, `betapdf`, `betarnd`
`betastat`, `cdf`, `chi2cdf`, `chi2inv`, `chi2pdf`, `chi2rnd`
`chi2stat`, `evcdf`, `evfit`, `evinv`, `evlike`, `evpdf`
`evrnd`, `evstat`, `expcdf`, `expfit`, `expinv`, `explike`
`exppdf`, `exprnd`, `expstat`, `fcdf`, `finv`, `fpdf`
`frnd`, `fstat`, `gamcdf`, `gamfit`, `gaminv`, `gamlike`
`gampdf`, `gamrnd`, `gamstat`, `gevcdf`, `gevfit`, `gevinv`
`gevlike`, `gevpdf`, `gevrnd`, `gevstat`, `gpcdf`, `gpfit`
`gpinv`, `gplike`, `gppdf`, `gprnd`, `gpstat`, `icdf`
`logncdf`, `lognfit`, `logninv`, `lognlike`, `lognpdf`, `lognrnd`
`lognstat`, `mle`, `ncfcdf`, `ncfinv`, `ncfpdf`, `ncfrnd`
`ncfstat`, `nctcdf`, `nctinv`, `nctpdf`, `nctrnd`, `nctstat`
`ncx2cdf`, `ncx2inv`, `ncx2pdf`, `ncx2rnd`, `ncx2stat`, `normcdf`
`normfit`, `norminv`, `normlike`, `normpdf`, `normrnd`, `normstat`
`pdf`, `random`, `raylcdf`, `raylfit`, `raylinv`, `raylpdf`
`raylrnd`, `raylstat`, `tcdf`, `tinv`, `tpdf`, `trnd`
`tstat`, `unifcdf`, `unifinv`, `unifit`, `unifpdf`, `unifrnd`
`unifstat`, `wblcdf`, `wblfit`, `wblinv`, `wbllike`, `wblpdf`
`wblrnd`, `wblstat`

### Discrete distributions — 35

Six families — binomial, Poisson, geometric, hypergeometric, negative binomial and discrete uniform —
each with its probability, distribution function, quantile, random draw and moments, plus the three
fitters MathWorks documents and the multinomial pair. A discrete family is described by the same
record a continuous one is, so the generic `pdf`, `cdf`, `icdf` and `random` names reach these too,
and `'upper'` works on every distribution function. No parameter has a documented default, so all of
them are required by name. Every quantile is the least value the variable can take whose distribution
function has reached the probability asked for, found by search rather than by rounding a formula.

`binocdf`, `binofit`, `binoinv`, `binopdf`, `binornd`, `binostat`
`geocdf`, `geoinv`, `geopdf`, `geornd`, `geostat`, `hygecdf`
`hygeinv`, `hygepdf`, `hygernd`, `hygestat`, `mnpdf`, `mnrnd`
`nbincdf`, `nbinfit`, `nbininv`, `nbinpdf`, `nbinrnd`, `nbinstat`
`poisscdf`, `poissfit`, `poissinv`, `poisspdf`, `poissrnd`, `poisstat`
`unidcdf`, `unidinv`, `unidpdf`, `unidrnd`, `unidstat`

### Multivariate distributions, sampling and resampling — 19

The distributions of a vector rather than a number, and the three ways of choosing points. Every one
reads its data as one observation per row and one variable per column. The multivariate probabilities
are quadratures rather than closed forms, so their second output is an error estimate and their
dimension is capped; everything that draws takes the one stream that rng seeds, so a seeded script repeats
itself. The resampling three call back into the script, because their subject is a function the caller
wrote, and each resample re-indexes every data argument by the same rows.

`bootci`, `bootstrp`, `cholcov`, `combnk`, `datasample`, `iwishrnd`
`jackknife`, `lhsdesign`, `lhsnorm`, `mvksdensity`, `mvncdf`, `mvnpdf`
`mvnrnd`, `mvtcdf`, `mvtpdf`, `mvtrnd`, `randg`, `randsample`
`wishrnd`

### Hypothesis tests and analysis of variance — 29

Every test answers the same four things — whether the null hypothesis is rejected at the level asked
for, how improbable the data would be if it held, an interval for whatever was being tested, and a
structure holding the statistic itself — and takes `'Alpha'` and `'Tail'` wherever MathWorks documents
them. The output *order* is MathWorks' and is not uniform: the parametric tests lead with the decision,
the three rank tests of location lead with the probability, and `vartestn`, `dwtest` and `linhyptest`
report no decision at all. The tests of a mean or a variance work column by column when handed a matrix,
so their decision and probability come back one per column and their interval two rows tall. Where a
small sample has an exact null distribution — the rank tests, the runs test, the two-by-two table — it
is counted rather than approximated, and above the cut-off the normal approximation takes over with the
tie and continuity corrections it calls for.

`adtest`, `anova1`, `anova2`, `anovan`, `ansaribradley`, `barttest`
`chi2gof`, `dwtest`, `fishertest`, `friedman`, `jbtest`, `kruskalwallis`
`kstest`, `kstest2`, `lillietest`, `linhyptest`, `manova1`, `multcompare`
`ranksum`, `runstest`, `sampsizepwr`, `signrank`, `signtest`, `ttest`
`ttest2`, `vartest`, `vartest2`, `vartestn`, `ztest`

### Regression — 24

Every one of these fits arrays and answers arrays, which is the line the milestone draws: a name whose
deliverable is a fitted model object is excluded with a reason, and the deterministic core it would have
wrapped is reached through the name here instead. `regress`, `regstats` and `leverage` take the design
matrix the caller built, intercept column and all, because a model without an intercept is a real model
and a function that quietly added one would put it out of reach; `robustfit`, `glmfit` and
`stepwisefit` add theirs, because their documentation says they do, and each takes the word that turns
it off.

The outputs are in MathWorks' order and the structures carry the field names their documentation lists.
`robustfit` takes all nine weight functions at their documented tuning constants; `glmfit` takes the
five error distributions and eight links, an offset, prior weights and a binomial response written
either as a proportion or as a count beside its trials; `mnrfit` takes the nominal, ordinal and
hierarchical models, with separate or shared slopes and four links between them; `coxphfit` takes
censoring, frequencies, a baseline and either tie rule.

`coxphfit`, `dummyvar`, `glmfit`, `glmval`, `hougen`, `invpred`
`lasso`, `lassoglm`, `leverage`, `mnrfit`, `mnrval`, `mvregress`
`mvregresslike`, `nlinfit`, `nlparci`, `nlpredci`, `plsregress`, `polyconf`
`regress`, `regstats`, `ridge`, `robustfit`, `stepwisefit`, `x2fx`

### Clustering, distances and multivariate analysis — 36

How far apart observations are, the groups that follow from it, and the directions a cloud of them
varies in. Everything reads one observation per row. The twelve documented distance words are shared
by every name that measures one, each with the extra argument its own metric needs — the Minkowski
exponent, the standardizing scale, the Mahalanobis covariance — and a set of pairwise distances
travels between these names as MathWorks' condensed row vector, which `linkage`, `cluster` and
`silhouette` tell apart from raw observations by shape rather than by a word.

Everything that starts from a guess draws from the one seeded stream, so a seeded script repeats
itself: the k-means++ choice of first centres, the random subsets `robustcov` concentrates from, the
starting factors of `nnmf`, and the sequences `hmmgenerate` draws.

`canoncorr`, `cluster`, `clusterdata`, `cmdscale`, `confusionmat`, `cophenet`
`dbscan`, `grp2idx`, `hmmdecode`, `hmmestimate`, `hmmgenerate`, `hmmtrain`
`hmmviterbi`, `inconsistent`, `kmeans`, `kmedoids`, `knnsearch`, `linkage`
`mahal`, `nnmf`, `onehotdecode`, `onehotencode`, `optimalleaforder`, `pca`
`pcacov`, `pcares`, `pdist`, `pdist2`, `ppca`, `procrustes`
`rangesearch`, `robustcov`, `rotatefactors`, `silhouette`, `spectralcluster`, `squareform`

### Distribution objects — 40

A distribution as a value: built from parameters by `makedist` or fitted to data by `fitdist`,
conditioned on an interval by `truncate`, and asked about by the same names that take a distribution
as a word. Twenty-nine classes, of which twenty-eight can be made by name and one — the kernel
smoothing — only fitted. Each publishes the properties MathWorks documents, and the class question
answers the package-qualified class name.

Thirteen names live under the prob package in the documentation and are counted here rather than
as methods, because they are the generic interface: `cdf`, `icdf`, `pdf` and `random` were already
implemented in waves C and D and are counted there, and the nine remaining — `mean`, `median`, `std`,
`var`, `iqr`, `truncate`, `negloglik`, `paramci`, `proflik` — arrive here. The first five keep
everything they did for data; the object check stands in front of them rather than replacing them.

`BetaDistribution`, `BinomialDistribution`, `BirnbaumSaundersDistribution`, `BurrDistribution`, `ExponentialDistribution`, `ExtremeValueDistribution`
`fitdist`, `GammaDistribution`, `GeneralizedExtremeValueDistribution`, `GeneralizedParetoDistribution`, `HalfNormalDistribution`, `InverseGaussianDistribution`
`iqr`, `KernelDistribution`, `LogisticDistribution`, `LoglogisticDistribution`, `LognormalDistribution`, `LoguniformDistribution`
`makedist`, `mean`, `median`, `MultinomialDistribution`, `NakagamiDistribution`, `NegativeBinomialDistribution`
`negloglik`, `NormalDistribution`, `paramci`, `PiecewiseLinearDistribution`, `PoissonDistribution`, `proflik`
`RayleighDistribution`, `RicianDistribution`, `StableDistribution`, `std`, `tLocationScaleDistribution`, `TriangularDistribution`
`truncate`, `UniformDistribution`, `var`, `WeibullDistribution`

### Designs of experiments, process capability and options — 10

The designs whose run list is enumerable rather than searched: the full factorial and its two-level
form, the fractions and the generator search behind them, and the two response-surface designs. The
iterative exchange algorithms that search for a design are excluded, because their answer depends on
where the search started. Beside them, the two questions a measured process answers about itself, and
the options structure the iterative names take.

`bbdesign`, `capability`, `ccdesign`, `ff2n`, `fracfact`, `fracfactgen`
`fullfact`, `gagerr`, `statget`, `statset`

### Copulas — 6

The dependence between variables, separated from what each of them is on its own. Two elliptical
families in any number of dimensions and three Archimedean ones which are, as MathWorks documents
them, bivariate. Rank correlation goes both ways for all five.

`copulacdf`, `copulafit`, `copulaparam`, `copulapdf`, `copularnd`, `copulastat`

### Simulation, search structures and the piecewise distribution — 10

Drawing from a distribution described by its own quantiles or moments rather than by name; the two
Markov chains, which need a density only up to a constant; the covariance of an estimate, read off
the curvature of the likelihood; a distribution that is empirical in the middle and generalized
Pareto in each tail; the two neighbourhood searchers; and the stochastic embedding.

`createns`, `ExhaustiveSearcher`, `johnsrnd`, `KDTreeSearcher`, `mhsample`, `mlecov`
`paretotails`, `pearsrnd`, `slicesample`, `tsne`

### File readers and writers — 6

The toolbox's own plain-text formats, which the base readers do not cover: one name per line, a
rectangle of numbers wrapped in variable and case names, a tab-delimited file read into one field per
column — and the one binary among them, a SAS transport file, whose numbers are in the floating-point
format of a machine nobody has run since the 1970s.

`caseread`, `casewrite`, `tblread`, `tblwrite`, `tdfread`, `xptread`

### Plot verbs — 29

Every one of these draws out of the primitives the figure model already has — lines, patches,
scatters, bars and text — and none of them adds a plot object. What they carry is the arithmetic in
front of the drawing: which quartiles a box is built from, where a dendrogram's links go, what a
probability plot puts on its axis, how a performance curve trades one error rate against the other.

`addedvarplot`, `andrewsplot`, `biplot`, `boxplot`, `capaplot`, `cdfplot`
`dendrogram`, `glyphplot`, `gplotmatrix`, `gscatter`, `hist3`, `histfit`
`interactionplot`, `lassoPlot`, `lsline`, `maineffectsplot`, `manovacluster`, `multivarichart`
`normplot`, `normspec`, `parallelcoords`, `perfcurve`, `probplot`, `qqplot`
`rcoplot`, `refcurve`, `refline`, `scatterhist`, `wblplot`

## Not implemented — 0

Empty, as of wave J. Every documented name is either implemented above or excluded below with its
reason.

## Excluded — 204

`aoctool`, `BayesianOptimization`, `bayesopt`, `binScatterPlot`, `CalinskiHarabaszEvaluation`, `candexch`
`candgen`, `cell2dataset`, `ClassificationBaggedEnsemble`, `ClassificationDiscriminant`, `ClassificationECOC`, `ClassificationECOCCoderConfigurer`
`ClassificationEnsemble`, `ClassificationGAM`, `ClassificationKernel`, `ClassificationKNN`, `ClassificationLinear`, `ClassificationLinearCoderConfigurer`
`ClassificationNaiveBayes`, `ClassificationNeuralNetwork`, `ClassificationPartitionedECOC`, `ClassificationPartitionedEnsemble`, `ClassificationPartitionedGAM`, `ClassificationPartitionedKernel`
`ClassificationPartitionedKernelECOC`, `ClassificationPartitionedLinear`, `ClassificationPartitionedLinearECOC`, `ClassificationPartitionedModel`, `ClassificationSVM`, `ClassificationSVMCoderConfigurer`
`ClassificationTree`, `ClassificationTreeCoderConfigurer`, `classify`, `ClusterCriterion`, `CompactClassificationDiscriminant`, `CompactClassificationECOC`
`CompactClassificationEnsemble`, `CompactClassificationGAM`, `CompactClassificationNaiveBayes`, `CompactClassificationNeuralNetwork`, `CompactClassificationSVM`, `CompactClassificationTree`
`CompactGeneralizedLinearModel`, `CompactLinearModel`, `CompactRegressionEnsemble`, `CompactRegressionGAM`, `CompactRegressionGP`, `CompactRegressionNeuralNetwork`
`CompactRegressionSVM`, `CompactRegressionTree`, `CompactTreeBagger`, `confusionchart`, `controlchart`, `controlrules`
`cordexch`, `CoxModel`, `crossval`, `cvpartition`, `dataset`, `dataset2table`
`daugment`, `DaviesBouldinEvaluation`, `dcovary`, `designecoc`, `dfittool`, `distributionFitter`
`evalclusters`, `factoran`, `FeatureSelectionNCAClassification`, `FeatureSelectionNCARegression`, `FeatureTransformer`, `fitcauto`
`fitcdiscr`, `fitcecoc`, `fitcensemble`, `fitcgam`, `fitckernel`, `fitcknn`
`fitclinear`, `fitcnb`, `fitcnet`, `fitcox`, `fitcsvm`, `fitctree`
`fitensemble`, `fitglm`, `fitglme`, `fitgmdist`, `fitlm`, `fitlme`
`fitlmematrix`, `fitnlm`, `fitrauto`, `fitrensemble`, `fitrgam`, `fitrgp`
`fitrkernel`, `fitrlinear`, `fitrm`, `fitrnet`, `fitrsvm`, `fitrtree`
`fitsemigraph`, `fitsemiself`, `fitSVMPosterior`, `fscchi2`, `fscmrmr`, `fscnca`
`fsrftest`, `fsrnca`, `fsulaplacian`, `fsurfht`, `GapEvaluation`, `gencfeatures`
`GeneralizedLinearMixedModel`, `GeneralizedLinearModel`, `generateLearnerDataTypeFcn`, `genrfeatures`, `gline`, `gmdistribution`
`gname`, `haltonset`, `HamiltonianSampler`, `hmcSampler`, `hyperparameters`, `iforest`
`incrementalClassificationLinear`, `incrementalClassificationNaiveBayes`, `incrementalRegressionLinear`, `IsolationForest`, `learnerCoderConfigurer`, `lime`
`LinearMixedModel`, `LinearModel`, `loadCompactModel`, `loadLearnerForCoder`, `makecdiscr`, `mat2dataset`
`mdscale`, `nlintool`, `nlmefit`, `nlmefitsa`, `nominal`, `NonLinearModel`
`optimizableVariable`, `ordinal`, `polytool`, `qrandstream`, `randtool`, `ReconstructionICA`
`RegressionBaggedEnsemble`, `RegressionEnsemble`, `RegressionGAM`, `RegressionGP`, `RegressionKernel`, `RegressionLinear`
`RegressionLinearCoderConfigurer`, `RegressionNeuralNetwork`, `RegressionPartitionedEnsemble`, `RegressionPartitionedGAM`, `RegressionPartitionedKernel`, `RegressionPartitionedLinear`
`RegressionPartitionedModel`, `RegressionPartitionedSVM`, `RegressionSVM`, `RegressionSVMCoderConfigurer`, `RegressionTree`, `RegressionTreeCoderConfigurer`
`relieff`, `RepeatedMeasuresModel`, `rica`, `robustdemo`, `rowexch`, `rsmdemo`
`rstool`, `saveCompactModel`, `saveLearnerForCoder`, `SemiSupervisedGraphModel`, `SemiSupervisedSelfTrainingModel`, `sequentialfs`
`shapley`, `SilhouetteEvaluation`, `sobolset`, `sortClasses`, `sparsefilt`, `SparseFiltering`
`stepwise`, `stepwiseglm`, `stepwiselm`, `struct2dataset`, `surfht`, `table2dataset`
`templateDiscriminant`, `templateECOC`, `templateEnsemble`, `templateKernel`, `templateKNN`, `templateLinear`
`templateNaiveBayes`, `templateSVM`, `templateTree`, `testcholdout`, `testckfold`, `TreeBagger`

By reason:

- **Trained-model objects and the functions that build them** — every `fitc*` and `fitr*` trainer,
  the `Classification*` and `Regression*` classes with their `Compact*`, `*Partitioned*` and
  `*Bagged*` variants, `TreeBagger`, the nine `template*` builders, `fitensemble`,
  `fitSVMPosterior`, `classify`, `makecdiscr` and `designecoc`. The deliverable of each is a fitted
  model object whose whole value is the methods hanging off it — predict, loss, margin, resubLoss,
  kfoldPredict — which is a runtime JGraph has no value model for, and half a trained model is
  worse than none. `crossval` and `cvpartition` are excluded with them: both exist
  to feed a model object.
- **Regression model objects and the formula engine** — `fitlm`, `fitglm`, `fitnlm`, `stepwise*`
  builders' object forms and the `LinearModel` / `GeneralizedLinearModel` / `NonLinearModel`
  classes. These need a Wilkinson notation parser and a model-object runtime, and the deterministic
  core they wrap — least squares, the generalized linear fit, robust and nonlinear fitting — is
  reached instead through the array-in, array-out names in the working set above.
- **Repeated-measures models** — `fitrm` and `RepeatedMeasuresModel`. The deliverable is a fitted
  object whose value is the methods hanging off it — the repeated-measures analysis, the sphericity
  corrections, the marginal means, the comparison — and a within-subject design written in Wilkinson
  notation. The between-subject question it wraps is reached instead through the general analysis of
  variance, and half of a model object is worse than none.
- **Mixed-effects and Gaussian mixture models** — `fitlme`, `fitglme`, `fitlmematrix`, `nlmefit`,
  `nlmefitsa` and their two classes, plus `gmdistribution` and `fitgmdist`. Same reason: an EM or
  restricted-likelihood loop whose answer is an object.
- **Feature selection, extraction and engineering** — the `fsc*` / `fsr*` / `fsulaplacian` rankers,
  `relieff`, `sequentialfs`, the two NCA classes, `rica`, `sparsefilt` and the automated
  `gencfeatures` / `genrfeatures` pair. Each is a learner in its own right, and every one of them
  returns an object.
- **Incremental, semi-supervised and anomaly learning** — `incremental*`, `fitsemigraph`,
  `fitsemiself`, `iforest` and their model objects. Streaming and graph-based learners, same shape.
- **Hyperparameter optimization** — `bayesopt`, `BayesianOptimization`, `optimizableVariable` and
  `hyperparameters`. The surrogate-model loop only means anything with trainable models to feed it.
- **Code generation** — the seven `*CoderConfigurer` objects, `learnerCoderConfigurer`,
  `saveLearnerForCoder` / `loadLearnerForCoder` and the two removed `*CompactModel` names, plus
  `generateLearnerDataTypeFcn`. These emit C from MATLAB, which JGraph is not.
- **Interactive apps and teaching demos** — `dfittool`, `distributionFitter`, `randtool`,
  `polytool`, `rstool`, `nlintool`, `stepwise`, `aoctool`, `fsurfht`, `surfht`, `gline`, `gname`,
  `robustdemo` and `rsmdemo`. JGraph has its own figure inspector and console; these are MATLAB's.
- **The legacy dataset array** — `dataset`, `nominal`, `ordinal` and the six converters.
  MathWorks marks every one "(Not Recommended)" and points at the table type, which JGraph already
  has.
- **Quasi-random and Hamiltonian sampling** — `haltonset`, `sobolset`, `qrandstream`,
  `hmcSampler` and `HamiltonianSampler`. All four are stream and sampler objects, and the seeded
  stream JGraph does have (M52) is a different contract.
- **D-optimal design of experiments** — `rowexch`, `cordexch`, `candexch`, `candgen`, `daugment`
  and `dcovary`. Iterative exchange algorithms whose answer depends on the search path; the
  enumerable designs — full and two-level factorial, Box-Behnken, central composite and the
  fractional-factorial pair — are in the working set.
- **Cluster evaluation criteria** — `evalclusters` and the four `*Evaluation` classes with
  `ClusterCriterion`. The criterion values are computable; the interface is an object that holds a
  swept range of k and re-clusters on demand.
- **Statistical process control charts** — `controlchart` and `controlrules`. The chart is a
  figure-bound object with rule annotations; the two process-capability names, which answer
  numbers, are in the working set.
- **Named individually** — `factoran` and `mdscale` (iterative rotations and stress minimization
  whose local optimum a mirror would not reproduce), `fitcox` and `CoxModel` (the partial-likelihood
  model object; the proportional-hazards fitting function that returns coefficients is in the
  working set), `binScatterPlot` (a tall-array display), `confusionchart` with `sortClasses` (a
  chart container), and `lime`, `shapley`, `testcholdout` and `testckfold` (model interpretation
  and model comparison, both of which need models).
## Recorded divergences

### Wave J: designs, files and the plot verbs

- **A probability plot puts the quantile on its vertical axis, not a probability scale.** MATLAB
  draws these on an axis whose tick positions are probabilities and whose spacing is the family's own
  quantile function. JGraph has no such scale, so `normplot`, `wblplot` and `probplot` plot the
  quantile itself and label the axis with what it is. The points and the reference line land in the
  same places; only the tick labels differ.
- **`hist3` draws a surface where MATLAB draws three-dimensional bars**, because the bars need a
  drawing primitive this build does not have. The counts are the same, and asking for them rather
  than for the picture gives exactly MathWorks' two outputs. `'Ctrs'` and `'Edges'`, which place the
  bins by hand, are refused by name; `'Nbins'` says how many to spread over the data.
- **`boxplot` draws the traditional box only.** The quartiles, the median, the whiskers at a
  multiple of the interquartile range, the outliers beyond them, and the notch — all of those are
  here, with `'Notch'`, `'Whisker'`, `'Labels'`, `'Positions'`, `'Widths'`, `'Colors'`, `'Symbol'`
  and `'Orientation'`. The options that change how the box is drawn rather than what it says —
  `'PlotStyle'`, `'BoxStyle'`, `'MedianStyle'`, `'Jitter'` and the rest — are refused by name.
- **Blocking a design is refused rather than guessed.** `bbdesign` and `ccdesign` accept
  `'center'`, and `ccdesign` accepts `'fraction'` and `'type'`, but `'blocksize'` is refused:
  splitting the runs into blocks confounds the block difference with an effect, and which effect is
  the experimenter's decision. Both names answer a second output that is all ones.
- **Box-Behnken designs beyond seven factors use every pair of factors.** The published designs run
  to seven; above that the construction here puts a two-level factorial in each pair, which is a
  valid design of the same shape but has more runs than MathWorks' table.
- **The default centre-point counts are the published uniform-precision ones.** They are a table in
  every source that gives them rather than a formula, and MathWorks does not document which table it
  uses; `'center'` says how many to use when the default is not wanted.
- **`fracfact` lists the constant among its confounding groups**, because in a fraction the constant
  is confounded with the defining word and that is worth reading. `fracfactgen` picks its own basic
  factors: the search over assignments is exhaustive, so naming them cannot help it, and naming them
  is refused rather than ignored.
- **The Archimedean copulas are bivariate**, as MathWorks documents them, and the elliptical pair
  work in any number of dimensions. Spearman's rank correlation of an Archimedean copula is
  integrated rather than interpolated from a table, which makes it exact where MATLAB's is
  interpolated; the two agree to about three digits.
- **`tsne` is the exact embedding, its step is bounded, and its scale is its own.** `'Algorithm'`,
  `'barneshut'` is refused by name — approximating the repulsion with a tree is a second
  approximation on top of the one the method already is. The learning rate defaults to a twentieth of
  the number of observations rather than to five hundred, and no point may move further in one step
  than the picture currently reaches: without both, a small set is thrown apart on its first step and
  never comes back. What the picture says about which points are near which is the answer; the
  coordinates themselves are not comparable with MATLAB's, and never were between two runs either.
- **The options structure is carried but barely read.** `statset` publishes every documented field
  and `statget` reads any of them back, so a script that sets one and passes it along keeps working.
  What actually reads one is `tsne`'s iteration count; every other name here has fixed tolerances,
  tight enough that the answer does not depend on them.
- **The two search structures answer the same question.** `createns`, `ExhaustiveSearcher` and
  `KDTreeSearcher` all build an object that `knnsearch` and `rangesearch` accept, and the object
  remembers which was asked for so that the class question answers it — but the search underneath is
  exhaustive either way, and both give exactly the same neighbours. `'BucketSize'` is remembered and
  changes nothing.
- **`paretotails` fits its middle empirically only.** MathWorks lets the middle be a kernel estimate
  or a function of the caller's; the tails are what the name is about, and the other two spellings
  are refused rather than quietly treated as the first.
- **Four verbs answer less than their whole documented output.** `lassoPlot` has no cross-validated
  form, because the penalized fit here does not compute one; `perfcurve` draws no confidence bands,
  because `'NBoot'` resamples; `mhsample` runs one chain at a time, so `'nchain'` is refused; and
  `glyphplot` draws stars, because a face glyph needs a drawing primitive this build lacks. `biplot`
  draws two components, and refuses three by name.
- **`xptread` reads version 5 transport files.** Every kind of missing value the format distinguishes
  reads as not-a-number, which is the only kind JGraph has.
- **A call written as a statement is told that nobody wanted its answer.** `ecdf`, `ksdensity`,
  `hist3` and `perfcurve` compute when something catches their outputs and draw when nothing does,
  which is what their documentation says and what could not be expressed before this wave: the
  interpreter now hands a builtin that asks for it an output count of zero. Nothing else in the
  language can tell the difference, and nothing else asks.


- **`range` is a name collision, not a missing function.** MATLAB's statistics `range(x)` answers
  `max(x) - min(x)`. JGS has had `range(start, stop, step)` since M12 — the Python-shaped sequence
  builder — and the JGS surface is frozen. The statistic is therefore registered in the MATLAB
  dialect only, and a JGS script keeps the sequence builder. This is the first case where the two
  dialects answer differently for the same name with the same arity.
- **`prctile` does not share JGS's `percentile`.** MATLAB places the sorted observations at the
  cumulative probabilities (i − ½)/n; JGS's own `percentile` places them at i/(n − 1), which is what
  NumPy and R's type 7 do. The two agree only where a percentile lands on an observation, so
  `prctile([1 2 3 4], 25)` is 1.5 here and `percentile` would answer 1.75. Both are kept, under
  their own names, because a script that asks for one and gets the other has no way to tell.
- **Which values a statistic drops is decided per name, because MATLAB decides it per name.**
  `prctile`, `quantile`, `skewness`, `kurtosis`, `mad` and `trimmean` discard NaN and shrink their
  denominator; `moment` and `zscore` let it propagate; `geomean` and `harmmean` take a `nanflag`;
  `range` ignores it because it is `max` minus `min` and both of those do. Nothing here filters
  unless its own documentation says it does.
- **Rank correlations use the approximate p-value everywhere.** Spearman's is Pearson's on the
  ranks, tested with Student's t; Kendall's uses the normal approximation to the variance of the
  concordance count. MATLAB computes both exactly below a small-sample cutoff, so the coefficients
  agree and the p-values differ in the third digit for a handful of observations.
- **`nearcorr` reaches the answer by alternating projections, not by Newton's method.** MATLAB's
  default `'Method'` is `'newton'`; both spellings are accepted and both name the same nearest
  matrix, which is what the tolerance is measured against — only the iteration count differs.
  `'Weights'` asks for the nearest matrix under a weighted norm and is refused by name rather than
  silently ignored.
- **`tabulate` always returns its table.** MATLAB prints the table when nothing catches it and
  returns nothing; here the matrix (or, for labels, the cell) is the answer either way, which the
  console then echoes. A script that assigned the result is unaffected.
- **`grpstats` answers numbers only.** MATLAB's third argument can be a confidence level that makes
  the no-output form draw error bars. The four documented outputs are here; the drawing is deferred
  with the rest of the plotting verbs.

- **The confidence-bound forms of the distribution functions are not accepted.** MATLAB lets
  `normcdf`, `gamcdf`, `logncdf`, `wblcdf`, `evcdf`, `expcdf` and their inverses take a parameter
  covariance matrix and a confidence level and answer three outputs — the probability and its
  limits. Those forms need a per-family delta-method expansion of the linear predictor, and each one
  is a separate piece of algebra that would be wrong in a way nothing here would catch. An extra
  argument in that slot is refused by name rather than ignored.
- **`gpfit` holds the threshold at zero, as MathWorks documents it.** The generalized Pareto family
  carries three parameters, but `gpfit` estimates two: letting the threshold float drives it to the
  smallest observation, where the likelihood is unbounded. `gplike` accepts all three and reports a
  two-by-two covariance for the two that were free.
- **A confidence interval is exact where MATLAB publishes an exact one and asymptotic otherwise.**
  The normal, lognormal, exponential, Rayleigh and uniform have closed-form intervals and get them.
  The rest read theirs off the observed information — the second derivative of the negative
  log-likelihood, differenced numerically — with a parameter that must stay positive given its
  interval on the logarithmic scale, so the lower limit is positive however wide the interval is.
  On a small sample these intervals are narrower than MATLAB's; the estimates themselves agree.
- **Censoring puts every family through the numerical fit.** A censored observation contributes its
  survival probability rather than its density, and none of the closed forms survive that. So the
  moment a censoring vector is present, even `normfit` maximizes its likelihood by simplex search
  and reports an asymptotic interval.
- **The `options` argument of a fitter is accepted and ignored.** It carries optimizer tolerances
  for a solver this build does not use; the tolerances here are fixed and tight enough that the
  estimate does not depend on them.
- **The noncentral quantiles are found by search, not by inversion.** `ncx2inv`, `nctinv` and
  `ncfinv` bracket the answer and bisect their own distribution function to full double precision.
  The answer is the quantile; only the way it was reached differs.
- **`'upper'` is the mirrored distribution function for the symmetric families and `1 − p`
  elsewhere.** `normcdf(x, mu, sigma, 'upper')` and `tcdf(x, v, 'upper')` are computed from the
  other side, so a tail at nine standard deviations keeps every significant figure. For the rest
  there is nothing better to do than subtract, and the far tail of, say, `gamcdf(…, 'upper')` is
  therefore only as accurate as the distribution function it was taken from.
- **`mle` with a density of your own reports an interval from numerical curvature.** The estimate
  comes from the same simplex search the named families use; the interval comes from differencing
  the log-likelihood the caller supplied. A density that is not smooth near its maximum will produce
  a wide interval or NaN rather than a wrong one.
- **A seeded run repeats itself; it does not repeat MATLAB.** Every `*rnd` name draws from the one
  stream `rng` seeds, so `rng(7)` twice gives the same numbers twice — but not MATLAB's numbers,
  for the reason ADR 0052 records about the generator.
- **A discrete quantile is the least value whose distribution function has reached `p`.** So
  `binoinv(0, n, p)` is 0, `poissinv(1, lambda)` is `Inf`, and `unidinv(0, n)` is 1 — the smallest
  value a discrete uniform can take — rather than the 0 that rounding `n*p` upwards would give.
- **The discrete distribution functions are integrals, not sums.** Each is written through a
  regularized incomplete beta or gamma, so `binocdf(3, 10, 0.3, 'upper')` and its siblings keep
  every figure a hundred rounded terms would lose. The hypergeometric has no such closed form and is
  summed from whichever end of its support is nearer.
- **`binofit` and `poissfit` report exact intervals, `nbinfit` an asymptotic one.** The first is
  Clopper and Pearson's, the second the chi-square interval on the total count — both defined by the
  tail probability they sit at rather than by a normal approximation. The negative binomial's shape
  has no closed form, so its interval comes from the curvature of the likelihood at the estimate.
- **A discrete fit does not take censored observations.** MathWorks documents censoring on the
  continuous fitters only, and a censoring vector handed to one of these three is refused by name
  rather than ignored.
- **`mle` fits four of the six discrete families.** Poisson, geometric and negative binomial go
  through their own fitters; the binomial needs `'ntrials'`, because the trial count is not part of
  the data. The hypergeometric and discrete uniform are refused with the reason: their parameters are
  population counts and a range, which a likelihood over the observations is not informative about.
- **A fitter whose third documented argument is an options structure ignores it.** `gevfit`, `gpfit`
  and `nbinfit` accept the argument and take no notice; only the fitters that document a fourth
  positional argument read slots three and four as censoring and frequency.

- **A multivariate probability is a quadrature, and its dimension is capped.** One and two variables
  have exact reductions and are used. Above that the integral goes through Genz's transformation and a
  tensor Gauss–Legendre rule, whose cost grows as a power of the dimension — so `mvncdf` takes up to
  five variables and `mvtcdf` up to four, and asks for more are refused by name rather than run for an
  hour. MATLAB integrates any dimension with a randomized quasi-Monte Carlo rule, whose answer changes
  slightly from call to call; this one does not, and the second output is the gap between the rule and
  a coarser one rather than a sampling error.
- **A separate covariance for every observation is not accepted.** `mvnpdf` and `mvncdf` take one
  covariance matrix, or a row of variances standing for a diagonal one. MathWorks also accepts a page
  of them, one per row of the data; that form is refused by name.
- **`mvksdensity` requires its bandwidth, and estimates the density only.** MathWorks requires the
  bandwidth too — there is no agreed rule of thumb in more than one dimension. The kernel is a product
  of the four one-dimensional ones, and the cumulative and inverse forms the univariate `ksdensity`
  offers have no unambiguous multivariate reading, so they are not offered.
- **`combnk` lists its combinations in ascending order.** MathWorks documents no order for `combnk` and
  points at nchoosek instead; this one answers what nchoosek would, which is the order a script can
  reason about.
- **`bootci` does not compute the studentized interval.** `'stud'` needs a bootstrap inside every
  bootstrap, or a standard error the statistic reports for itself. It is refused with the reason and
  the four intervals that are computed — the percentile, the bias-corrected percentile, the accelerated
  one, and the normal.
- **A Wishart draw uses Bartlett's decomposition wherever it applies.** More degrees of freedom than
  variables, whole or not, take the triangular construction; fewer, and only a whole number of them,
  fall back to the sum of outer products the definition gives, which is singular and correctly so. A
  fractional degree of freedom below the variable count has neither and is refused.
- **`lhsnorm` stratifies an ordinary draw rather than transforming a design.** A multivariate normal
  sample is drawn and its values are then moved, in rank order, onto the stratum midpoints of the same
  marginal. Ranks carry the correlation, so the covariance survives; the sample is not the one MATLAB
  would produce, for the reason every seeded answer here differs from MATLAB's.

- **A composite goodness-of-fit probability is read off a published table, not simulated.**
  Lilliefors' statistic and Anderson–Darling's have a different null distribution once the parameters
  are estimated from the same sample, and it has no closed form. MathWorks simulates one; this reads
  Stephens' published critical values and interpolates between them in the logarithm of the
  probability, which is smooth between the tabulated points and clamped to the range they cover — so a
  probability of 0.15 may mean "0.15 or more" and one of 0.01 may mean "0.01 or less". `'MCTol'` is
  refused by name rather than accepted and ignored, for the same reason. The one exception is the
  composite normal case of `adtest`, which has a published closed-form probability covering the whole
  range and uses it.
- **`jbtest` always refers its statistic to the limiting chi-square.** MathWorks uses a simulated table
  below two thousand observations and the chi-square above it; here the chi-square with two degrees of
  freedom is used throughout, so a small sample's probability is a little optimistic. The third
  argument, which asks for a simulation tolerance, is refused with the reason.
- **A rank test counts its null distribution below MathWorks' own cut-offs and approximates it above.**
  `ranksum` counts when the smaller sample is below ten and the two together below twenty, `signrank`
  at fifteen or fewer differences, `signtest` at a hundred or fewer, and `ansaribradley` at
  twenty-five or fewer observations — the same rules MathWorks documents. Ties make the count wrong
  rather than slow, because it enumerates arrangements of distinct ranks, so an exact test asked for
  over tied data is refused rather than answered. The approximation carries the half-step continuity
  correction and the tie correction to the variance.
- **`signtest` reports the number of positive differences.** MathWorks documents `stats.sign` as "the
  value of the sign test statistic" without saying which count it is; this one is the number of
  differences above zero, which is what the right-hand tail is computed from.
- **The analysis-of-variance names answer numbers and draw nothing.** `anova1`, `anova2`, `anovan`,
  `kruskalwallis`, `friedman`, `vartestn` and `multcompare` open a figure in MATLAB when nothing
  catches their output. Here the table is the second output, a cell array the console prints, and the
  display argument is read and accepted so that a script passing `'off'` is not told it is unexpected.
  `multcompare`'s third output is the figure handle in MATLAB and is an empty here.
- **`anovan` fits crossed, fixed, categorical factors.** `'nested'`, `'random'` and `'continuous'` each
  name a different model — a hierarchy, a variance component, and a covariate — and each would need its
  own error term rather than the residual one every F here is measured against. All three are refused
  by name. The rest of the surface is there: the three sums of squares, the model as a word, an
  interaction order or a term matrix, and named variables.
- **`multcompare`'s studentized range is a quadrature.** Tukey and Kramer's correction needs the
  distribution of the largest gap between several means, which is a double integral with no closed form
  above two means. It is computed with the same Gauss–Legendre rule the multivariate probabilities use,
  over ranges taken from the quantiles of the distributions involved; at two means it reproduces the
  exact identity — √2 times a Student's t — to eight figures, and the published tables to the two
  decimals they are given to.
- **`kstest`'s distribution is a function or a table, not an object.** MathWorks accepts a probability
  distribution object; those arrive with the distribution objects in a later wave. A two-column matrix
  of points and probabilities works as documented, read by interpolation, and a function handle is
  accepted in the object's place.
- **`dwtest`'s exact probability comes from Imhof's inversion.** The Durbin–Watson statistic is a ratio
  of quadratic forms whose distribution depends on the design matrix, and the event "the statistic is
  below d" is exactly "a particular weighted sum of squared normals is below zero". That probability is
  an integral of the characteristic function, and it is computed rather than tabulated. The
  approximation matches the first two moments of the same ratio.
- **`barttest` uses the classical correction factor.** The statistic is
  `(n − 1 − (2p + 11)/6)` times the log ratio of the arithmetic and geometric means of the remaining
  eigenvalues. MathWorks does not document which correction it applies, and the two differ in the
  second digit of the statistic on a small sample; the dimension they report is the same.
- **`fishertest`'s interval is the normal one on the logarithm of the odds ratio.** MathWorks documents
  it as asymptotic and so is this. The probability itself is exact — every table with the same margins
  is enumerated, and the two-sided one adds up every table no more likely than the one seen.
- **`sampsizepwr` finds a sample size by search.** The power is computed in closed form for each test
  and the size is the smallest one that reaches it, found by doubling and then bisecting. For the exact
  binomial test the power is not monotone in the sample size — it steps as the critical count moves —
  so the search walks back one observation at a time to make sure the answer really is the smallest.

- **A rank-deficient design gets the minimum-norm coefficients, not a basic solution.** Every fit in
  the regression wave factors the design rather than inverting its cross-product, so a design with two
  ways of writing the same predictor answers a fit instead of dividing by nearly nothing. MATLAB picks
  a subset of columns and zeroes the rest; the two answers differ, but the fitted values, the
  residuals and every statistic derived from them agree, because those depend only on the space the
  columns span.
- **`regress`'s residual intervals use the residual degrees of freedom, not one fewer.** The interval
  around each residual is built from the error variance re-estimated without that observation — which
  is the point, or a single wild point would inflate the very spread meant to reveal it — and the
  multiplier is Student's t on the model's own degrees of freedom.
- **`robustfit` reports the ordinary scale where the robust one degenerates to zero.** A fit that
  passes exactly through everything it still trusts leaves no spread to measure, and the median
  absolute residual is then rounding error rather than a scale. Below a noise floor it is treated as
  zero: the weights become the ones and zeros that limit calls for, and the scale reported is the
  spread of the observations the fit kept, or the ordinary one if that is zero too.
- **The generalized linear fit is Fisher scoring throughout.** Where the link is the family's
  canonical one that is exactly Newton's method on the likelihood; where it is not, it is the
  expected-information variant, which reaches the same answer by a slightly different route and can
  differ in the last iteration's worth of digits.
- **`nlinfit`'s Jacobian is taken by forward differences, and every interval after it is the linear
  formula with that Jacobian substituted in.** A model given as a script function has no derivative to
  hand. The intervals are therefore the linearization's, which is optimistic for a badly curved model
  — that is the approximation `nlparci` and `nlpredci` are documented to make, and it is not corrected
  here. `'ErrorModel'` takes `'constant'`, `'proportional'` and `'exponential'`; `'combined'` needs
  the two error parameters fitted alongside the model and is refused by name.
- **The penalized paths report the fit's own error, not a cross-validated one.** `lasso` and
  `lassoglm` answer the coefficients, the penalties, the count of non-zero coefficients and the
  criterion at each penalty. `'CV'` is refused by name rather than answered with the training error
  under a name that means the held-out one, and `'PredictorNames'` is refused because nothing here
  reports by name. The same applies to `plsregress`, whose error table is the fit's own.
- **`plsregress` extracts its components by SIMPLS.** De Jong's method deflates the cross-product
  rather than the data matrices. With as many components as predictors it reproduces ordinary least
  squares exactly, which is what the implementation is pinned against.
- **`mnrfit`'s curvature is differenced from an exact gradient.** The three models' second derivatives
  are three different pieces of bookkeeping over the same small parameter vector; differencing the
  analytic gradient is accurate to about half of working precision, several digits more than the
  standard errors it feeds are worth. `coxphfit` does the same. A nominal model takes only the logit,
  because it has one probability per category rather than a single one to transform.
- **`mvregress` fits by alternating generalized least squares and answers the maximum-likelihood
  covariance.** The error covariance is the residual cross-product over the number of observations,
  not over the residual degrees of freedom. A shared design matrix means each response has its own
  coefficients over the same columns, so the answer is one column of coefficients per response, and
  the loop then converges immediately to fitting each response on its own. `'algorithm'` is accepted
  and the three spellings name the same estimator.
- **`coxphfit`'s baseline hazard is Breslow's estimator under either tie rule.** The rule chosen
  changes the coefficients — Efron's shrinks the risk set across tied failures and is the less biased
  of the two — but the cumulative hazard is accumulated the same way afterwards.
- **`stepwisefit` stops once nothing is left to explain.** A response some predictor already reproduces
  exactly leaves residuals of rounding error, whose ratio to a standard error that is also rounding
  error is a perfectly ordinary-looking statistic; below a noise floor the search declines to read that
  as evidence. `'display'` can only be `'off'` — nothing in this surface is drawn, and the numbers a
  display would show are the ones already answered.

- **A distribution object is a struct that knows what class it is.** MathWorks' distributions are
  value classes, so a copy that is truncated must leave the original alone; a struct gives that where
  a registry handle would have to be taught it. Two things follow. A script can assign to a property
  — `pd.mu = 3` — which MathWorks refuses; the object stays consistent, because every function reads
  the parameters back out of the struct, but nothing stops the write. And the struct carries a `Type`
  field holding the class name, which MathWorks' objects do not have, so it shows up in a field list.
  The class question answers the package-qualified name either way, and a structure test answers yes
  where MathWorks answers no.
- **The class names construct as well as describe.** MathWorks documents `makedist` and `fitdist` as
  the way to build a distribution and says nothing about calling the class. Here the class name is
  also a constructor taking the same name-value pairs, which is purely additive: a spelling that
  errored now works, and no spelling that worked has changed.
- **A truncated mean and variance are integrated, not derived.** Conditioning on an interval destroys
  every closed form for the moments, so they are integrated over probability rather than over the
  variable — which keeps the range finite even where the interval is not. The mesh is graded towards
  both ends, where the quantile is steepest; on the families here that reproduces the closed forms
  that do exist to about six figures, and a distribution whose truncated tail is heavy enough to
  matter will be less accurate than that.
- **`fitdist` fits a piecewise-linear distribution to the empirical distribution function.** Every
  distinct observation becomes a breakpoint, and the height at it is the proportion at or below.
  MathWorks does not document what `fitdist(x, 'PiecewiseLinear')` fits; this is the reading that
  makes the fitted object reproduce the sample it came from.
- **A kernel fit's default bandwidth is Silverman's rule.** The scale is the smaller of the standard
  deviation and the interquartile range over 1.349, times 1.06 n^(-1/5). Naming `'Width'` overrides
  it, and the four documented kernels — normal, box, triangle and epanechnikov — each carry their own
  variance into the smoothed spread, which is why the standard deviation of a kernel fit exceeds the
  standard deviation of the sample it smoothed.
- **`paramci` asks the fitter again rather than reading a stored covariance.** The interval comes back
  from the same code that produced the estimate, at the confidence requested — so a family with an
  exact interval keeps it, and there is no second formula here to disagree with the first. It needs
  the data, so it refuses an object built by `makedist`.
- **`proflik` walks forty-one points and re-fits at each.** The range, when not named, spans three
  standard errors either side of the estimate, cut off above zero for a parameter that must stay
  positive. A profile can therefore report a likelihood slightly above the one at the fitted
  parameters, because several families' fitters return the unbiased estimate where the profile
  maximizes the likelihood itself.
- **`makedist` knows the twenty-eight classes MathWorks lets it build.** The chi-square, t, F,
  geometric, hypergeometric, discrete uniform and noncentral families have no distribution object in
  the documentation and have none here; they are reached by name, as `pdf('Chi-square', …)`.

## Answers this mirror will state rather than match

Reserved for the numbers where a faithful mirror is not possible or not worth it — iterative
optimizer tolerances, bounded numerics, seeding. Filled in as the waves land.

- **`ksdensity`'s inverse is read off a grid.** The cumulative curve is evaluated at 2,048 points
  spanning the sample plus four bandwidths, and the requested probability is interpolated along it.
  That is accurate to the grid spacing rather than to the root-finding tolerance MATLAB uses, and
  when no evaluation points are named the default is 100 probabilities evenly spaced across [0, 1].
- **`ecdf`'s confidence bounds are Greenwood's on the plain scale.** The standard error is
  Greenwood's formula and the interval is symmetric about the estimate, then clipped to [0, 1] — not
  transformed onto a log or log-log scale first. The bounds therefore agree with MATLAB's in the
  middle of the curve and pull in at the ends.

- **The minimum-norm choice reappears in `pca` and stays out of `pcacov`.** The components come from a
  singular value decomposition of the centred data rather than from the eigenvectors of its
  covariance, because forming the covariance squares the condition number and a nearly collinear data
  set — which is what anyone runs this on — loses half its digits in the forming. `pcacov` exists for
  the caller who has only the covariance and takes that loss knowingly, so the two names can differ in
  the last few digits of a small eigenvalue.
- **A component's sign is chosen, not left to the arithmetic.** Negating a loading vector and its
  scores describes the same direction, so `pca`, `pcacov`, `ppca`, `cmdscale` and `rotatefactors` each
  turn every component until its largest-magnitude entry is positive. MathWorks makes no such promise,
  so a column here may be the negative of MATLAB's — with the scores negated to match, which is what
  keeps the reconstruction identical.
- **`pca` will not pad its answer with components the data does not support.** `'Economy'` off asks
  for as many columns as there are variables, filled out with zeros past the rank; that is refused by
  name, because those columns carry no variance and span nothing. `'Rows','pairwise'` is refused for a
  sharper reason: a covariance assembled from whatever pairs happen to be observed need not be a
  covariance at all. Dropping incomplete rows is the default, and `ppca` models the gaps instead.
- **`kmeans` measures in the squared Euclidean distance only.** MATLAB's other four distances each
  need a different centre — the median for cityblock, the normalized mean for cosine — and a mean
  paired with a distance it does not minimize would report a total that nothing minimizes. `kmedoids`,
  whose centre is a member of the cluster whatever the metric, takes all twelve distance words.
- **A non-negative factorization finds a local minimum, and the two algorithms find different ones.**
  The alternating solve clamps an unconstrained least-squares step, which is fast and can stall; the
  multiplicative update descends monotonically and is slower. Neither is wrong and MathWorks documents
  the same dependence — `'Replicates'` is the answer to it, and the two algorithms are worth trying
  against each other on a matrix that matters.
- **`spectralcluster` builds the Gaussian-kernel graph only.** The nearest-neighbour similarity graph
  and the random-walk Laplacian are refused by name rather than substituted; `'KernelScale'` sets the
  kernel's width, and left out it is the median pairwise distance, which puts a typical pair at an
  affinity of about six tenths.
- **`knnsearch` and `rangesearch` always search exhaustively.** `'NSMethod'` and `'BucketSize'` choose
  between search structures that trade exactness for speed; there is nothing to choose here, so both
  are refused by name. `'SortIndices'` cannot be turned off for the same reason — the neighbours always
  come back nearest first. `'IncludeTies'` is refused with a pointer to `rangesearch`, which returns
  exactly the variable-length neighbourhood it asks for.
- **A tie between two equally distant points is broken by which came first.** Nothing in a sort
  promises that, so the comparison says so explicitly, in `knnsearch`, `rangesearch`, `linkage` and the
  concentration step of `robustcov`. The answers are therefore reproducible rather than merely correct.
- **The centroid and median trees may invert, and the inversion is left in.** A merged centroid can sit
  closer to a third cluster than either parent did, which makes a merge height smaller than the one
  below it. MathWorks leaves those in rather than hiding them, and so does this — but `cluster` orders
  the merges by height before cutting into k groups, so the cut still means "the k tightest".
- **`silhouette` returns its values rather than drawing them.** MathWorks draws the silhouette when
  nothing catches the answer; the plotting verbs are deferred with the rest of wave J, so the values
  are the answer either way and the console echoes them — the same choice `tabulate` made in wave B.
- **`robustcov` corrects for its own selection and reports no bias correction beyond that.** The
  covariance of the tightest half is too small by a known factor because the half was chosen for being
  tight, and that consistency factor is applied. MathWorks' small-sample bias correction is a separate
  table of fitted constants and is not applied; on a sample of any size worth running this on the two
  agree, and on a very small one the estimate here is slightly the tighter.
- **A hidden Markov model moves before it emits.** MathWorks' generator takes a transition step first,
  so the state it was handed is where the walk begins and never where an observation came from; the
  forward-backward recursion here is written over one extra step for the same reason, which is why
  `hmmdecode` answers a matrix one column wider than the sequence. Getting this backwards would shift
  every sequence by one step and quietly change every estimate downstream.
- **`hmmtrain` estimates by Baum-Welch only.** Viterbi training assigns each observation to its single
  most likely state, which is a different estimator with a different answer, and it is refused by name
  rather than silently replaced by the one that weights every path.
- **A state the data never visited keeps an empty row.** `hmmestimate` and `hmmtrain` normalize each
  row by its own total, and a row with no evidence in it stays at zero rather than being filled with a
  uniform distribution the data never showed. Pseudocounts are how a caller says otherwise.
- **A stable distribution's density and distribution function are quadratures.** Only the normal, the
  Cauchy and the Lévy have a density that can be written down; the first two are answered in closed
  form and everything else comes from Nolan's integral over an angle, cut at the places where the
  exponent crosses a fixed ladder of values so that a peak a millionth of a radian wide is still
  bracketed. That reproduces the Lévy to six figures and is continuous across a stability index of
  one; it is not the last-digit answer a dedicated stable library would give, and a script asking for
  a thousand densities will notice that each one is an integral.
