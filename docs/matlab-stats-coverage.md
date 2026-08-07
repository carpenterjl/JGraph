# MATLAB Statistics and Machine Learning Toolbox coverage

**31 of 589 documented** Statistics and Machine Learning Toolbox names are implemented, as of
M53 wave B. Wave A built the scaffold — the list, this document and its verifier; wave B added the
descriptive and robust statistics.

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

## Implemented — 31

The descriptive and robust statistics, and the correlations. Every one takes its documented option
surface: a leading flag where the documentation puts one, then a dimension, `'all'`, or nothing.

`corr`, `corrcov`, `crosstab`, `ecdf`, `ecdfhist`, `geomean`
`grpstats`, `harmmean`, `ksdensity`, `kurtosis`, `mad`, `moment`
`nancov`, `nanmax`, `nanmean`, `nanmedian`, `nanmin`, `nanstd`
`nansum`, `nanvar`, `nearcorr`, `partialcorr`, `partialcorri`, `prctile`
`quantile`, `range`, `skewness`, `tabulate`, `tiedrank`, `trimmean`
`zscore`

## Not implemented — 358

The rest of the milestone's working set, in the order the waves take it: the continuous and discrete
distribution families, multivariate distributions, random sampling and resampling, hypothesis tests
and ANOVA, regression, clustering and multivariate analysis, the distribution objects, and the
plotting verbs.

`addedvarplot`, `adtest`, `andrewsplot`, `anova1`, `anova2`, `anovan`
`ansaribradley`, `barttest`, `bbdesign`, `betacdf`, `BetaDistribution`, `betafit`
`betainv`, `betalike`, `betapdf`, `betarnd`, `betastat`, `binocdf`
`binofit`, `binoinv`, `BinomialDistribution`, `binopdf`, `binornd`, `binostat`
`biplot`, `BirnbaumSaundersDistribution`, `bootci`, `bootstrp`, `boxplot`, `BurrDistribution`
`canoncorr`, `capability`, `capaplot`, `caseread`, `casewrite`, `ccdesign`
`cdf`, `cdfplot`, `chi2cdf`, `chi2gof`, `chi2inv`, `chi2pdf`
`chi2rnd`, `chi2stat`, `cholcov`, `cluster`, `clusterdata`, `cmdscale`
`combnk`, `confusionmat`, `cophenet`, `copulacdf`, `copulafit`, `copulaparam`
`copulapdf`, `copularnd`, `copulastat`, `coxphfit`, `createns`, `datasample`
`dbscan`, `dendrogram`, `dummyvar`, `dwtest`, `evcdf`, `evfit`
`evinv`, `evlike`, `evpdf`, `evrnd`, `evstat`, `ExhaustiveSearcher`
`expcdf`, `expfit`, `expinv`, `explike`, `ExponentialDistribution`, `exppdf`
`exprnd`, `expstat`, `ExtremeValueDistribution`, `fcdf`, `ff2n`, `finv`
`fishertest`, `fitdist`, `fitrm`, `fpdf`, `fracfact`, `fracfactgen`
`friedman`, `frnd`, `fstat`, `fullfact`, `gagerr`, `gamcdf`
`gamfit`, `gaminv`, `gamlike`, `GammaDistribution`, `gampdf`, `gamrnd`
`gamstat`, `GeneralizedExtremeValueDistribution`, `GeneralizedParetoDistribution`, `geocdf`, `geoinv`, `geopdf`
`geornd`, `geostat`, `gevcdf`, `gevfit`, `gevinv`, `gevlike`
`gevpdf`, `gevrnd`, `gevstat`, `glmfit`, `glmval`, `glyphplot`
`gpcdf`, `gpfit`, `gpinv`, `gplike`, `gplotmatrix`, `gppdf`
`gprnd`, `gpstat`, `grp2idx`, `gscatter`, `HalfNormalDistribution`, `hist3`
`histfit`, `hmmdecode`, `hmmestimate`, `hmmgenerate`, `hmmtrain`, `hmmviterbi`
`hougen`, `hygecdf`, `hygeinv`, `hygepdf`, `hygernd`, `hygestat`
`icdf`, `inconsistent`, `interactionplot`, `InverseGaussianDistribution`, `invpred`, `iqr`
`iwishrnd`, `jackknife`, `jbtest`, `johnsrnd`, `KDTreeSearcher`, `KernelDistribution`
`kmeans`, `kmedoids`, `knnsearch`, `kruskalwallis`, `kstest`, `kstest2`
`lasso`, `lassoglm`, `lassoPlot`, `leverage`, `lhsdesign`, `lhsnorm`
`lillietest`, `linhyptest`, `linkage`, `LogisticDistribution`, `LoglogisticDistribution`, `logncdf`
`lognfit`, `logninv`, `lognlike`, `LognormalDistribution`, `lognpdf`, `lognrnd`
`lognstat`, `LoguniformDistribution`, `lsline`, `mahal`, `maineffectsplot`, `makedist`
`manova1`, `manovacluster`, `mean`, `median`, `mhsample`, `mle`
`mlecov`, `mnpdf`, `mnrfit`, `mnrnd`, `mnrval`, `multcompare`
`MultinomialDistribution`, `multivarichart`, `mvksdensity`, `mvncdf`, `mvnpdf`, `mvnrnd`
`mvregress`, `mvregresslike`, `mvtcdf`, `mvtpdf`, `mvtrnd`, `NakagamiDistribution`
`nbincdf`, `nbinfit`, `nbininv`, `nbinpdf`, `nbinrnd`, `nbinstat`
`ncfcdf`, `ncfinv`, `ncfpdf`, `ncfrnd`, `ncfstat`, `nctcdf`
`nctinv`, `nctpdf`, `nctrnd`, `nctstat`, `ncx2cdf`, `ncx2inv`
`ncx2pdf`, `ncx2rnd`, `ncx2stat`, `NegativeBinomialDistribution`, `negloglik`, `nlinfit`
`nlparci`, `nlpredci`, `nnmf`, `NormalDistribution`, `normcdf`, `normfit`
`norminv`, `normlike`, `normpdf`, `normplot`, `normrnd`, `normspec`
`normstat`, `onehotdecode`, `onehotencode`, `optimalleaforder`, `parallelcoords`, `paramci`
`paretotails`, `pca`, `pcacov`, `pcares`, `pdf`, `pdist`
`pdist2`, `pearsrnd`, `perfcurve`, `PiecewiseLinearDistribution`, `plsregress`, `poisscdf`
`poissfit`, `poissinv`, `PoissonDistribution`, `poisspdf`, `poissrnd`, `poisstat`
`polyconf`, `ppca`, `probplot`, `procrustes`, `proflik`, `qqplot`
`randg`, `random`, `randsample`, `rangesearch`, `ranksum`, `raylcdf`
`RayleighDistribution`, `raylfit`, `raylinv`, `raylpdf`, `raylrnd`, `raylstat`
`rcoplot`, `refcurve`, `refline`, `regress`, `regstats`, `RepeatedMeasuresModel`
`RicianDistribution`, `ridge`, `robustcov`, `robustfit`, `rotatefactors`, `runstest`
`sampsizepwr`, `scatterhist`, `signrank`, `signtest`, `silhouette`, `slicesample`
`spectralcluster`, `squareform`, `StableDistribution`, `statget`, `statset`, `std`
`stepwisefit`, `stepwiseglm`, `stepwiselm`, `tblread`, `tblwrite`, `tcdf`
`tdfread`, `tinv`, `tLocationScaleDistribution`, `tpdf`, `TriangularDistribution`, `trnd`
`truncate`, `tsne`, `tstat`, `ttest`, `ttest2`, `unidcdf`
`unidinv`, `unidpdf`, `unidrnd`, `unidstat`, `unifcdf`, `unifinv`
`unifit`, `UniformDistribution`, `unifpdf`, `unifrnd`, `unifstat`, `var`
`vartest`, `vartest2`, `vartestn`, `wblcdf`, `wblfit`, `wblinv`
`wbllike`, `wblpdf`, `wblplot`, `wblrnd`, `wblstat`, `WeibullDistribution`
`wishrnd`, `x2fx`, `xptread`, `ztest`

## Excluded — 200

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
`fitrkernel`, `fitrlinear`, `fitrnet`, `fitrsvm`, `fitrtree`, `fitsemigraph`
`fitsemiself`, `fitSVMPosterior`, `fscchi2`, `fscmrmr`, `fscnca`, `fsrftest`
`fsrnca`, `fsulaplacian`, `fsurfht`, `GapEvaluation`, `gencfeatures`, `GeneralizedLinearMixedModel`
`GeneralizedLinearModel`, `generateLearnerDataTypeFcn`, `genrfeatures`, `gline`, `gmdistribution`, `gname`
`haltonset`, `HamiltonianSampler`, `hmcSampler`, `hyperparameters`, `iforest`, `incrementalClassificationLinear`
`incrementalClassificationNaiveBayes`, `incrementalRegressionLinear`, `IsolationForest`, `learnerCoderConfigurer`, `lime`, `LinearMixedModel`
`LinearModel`, `loadCompactModel`, `loadLearnerForCoder`, `makecdiscr`, `mat2dataset`, `mdscale`
`nlintool`, `nlmefit`, `nlmefitsa`, `nominal`, `NonLinearModel`, `optimizableVariable`
`ordinal`, `polytool`, `qrandstream`, `randtool`, `ReconstructionICA`, `RegressionBaggedEnsemble`
`RegressionEnsemble`, `RegressionGAM`, `RegressionGP`, `RegressionKernel`, `RegressionLinear`, `RegressionLinearCoderConfigurer`
`RegressionNeuralNetwork`, `RegressionPartitionedEnsemble`, `RegressionPartitionedGAM`, `RegressionPartitionedKernel`, `RegressionPartitionedLinear`, `RegressionPartitionedModel`
`RegressionPartitionedSVM`, `RegressionSVM`, `RegressionSVMCoderConfigurer`, `RegressionTree`, `RegressionTreeCoderConfigurer`, `relieff`
`rica`, `robustdemo`, `rowexch`, `rsmdemo`, `rstool`, `saveCompactModel`
`saveLearnerForCoder`, `SemiSupervisedGraphModel`, `SemiSupervisedSelfTrainingModel`, `sequentialfs`, `shapley`, `SilhouetteEvaluation`
`sobolset`, `sortClasses`, `sparsefilt`, `SparseFiltering`, `stepwise`, `struct2dataset`
`surfht`, `table2dataset`, `templateDiscriminant`, `templateECOC`, `templateEnsemble`, `templateKernel`
`templateKNN`, `templateLinear`, `templateNaiveBayes`, `templateSVM`, `templateTree`, `testcholdout`
`testckfold`, `TreeBagger`

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
