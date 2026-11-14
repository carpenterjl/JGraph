// A script's hggroup or hgtransform is the model's own GraphicsGroupModel since open item 296
// (ADR 0360): the group lives in its axes' Groups, so the renderer draws its Visible and Matrix, the
// autoscaler fits through them and the .graph document keeps the group. The scripting layer keeps the
// name it has always used for it.
global using JgsGraphicsGroup = JGraph.Core.Model.GraphicsGroupModel;
