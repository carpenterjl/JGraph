using JGraph.Core.Model;
using JGraph.Serialization.Dto;

namespace JGraph.Serialization.Mapping;

/// <summary>
/// An axes' groups and transforms in the document (open item 296, ADR 0360). A group names its
/// members by where they are kept - the axes' plots, texts and lights, and the groups before it - so a
/// document read back puts every member in its group in the order it joined, the transform's matrix
/// and the group's visibility with it, and the group where it stood among the axes' children.
/// </summary>
internal static partial class FigureMapper
{
    private static List<GroupDto>? GroupsToDto(AxesModel axes)
    {
        IReadOnlyList<GraphicsGroupModel> groups = axes.Groups;
        if (groups.Count == 0)
        {
            return null;
        }

        var held = new HashSet<GraphObject>(ReferenceEqualityComparer.Instance);
        foreach (GraphicsGroupModel group in groups)
        {
            foreach (GraphObject member in group.Members)
            {
                held.Add(member);
            }
        }

        // The axes' own drawn objects, in the order a script's Children walk merges them (open item
        // 166): a group stands before the first of these made after it.
        var drawn = new List<GraphObject>();
        drawn.AddRange(axes.Plots.Where(plot => !held.Contains(plot)));
        drawn.AddRange(axes.Annotations.Where(text => !held.Contains(text)));
        drawn.AddRange(axes.Lights.Where(light => !held.Contains(light)));

        var dtos = new List<GroupDto>(groups.Count);
        foreach (GraphicsGroupModel group in groups)
        {
            var dto = new GroupDto
            {
                Transforms = group.Transforms,
                Tag = group.Tag,
                Visible = group.Visible,
                DisplayName = string.IsNullOrEmpty(group.DisplayName) ? null : group.DisplayName,
                Matrix = group.MovesMembers ? RowMajor(group.Matrix) : null,
                Outer = group.Outer is { } outer ? IndexOf(groups, outer) : -1,
            };

            foreach (GraphObject member in group.Members)
            {
                if (MemberRef(axes, groups, member) is { } named)
                {
                    dto.Members.Add(named);
                }
            }

            if (group.Outer is null && drawn.FirstOrDefault(other => other.CreationOrder >= group.Seat) is { } next)
            {
                dto.Before = MemberRef(axes, groups, next);
            }

            dtos.Add(dto);
        }

        return dtos;
    }

    private static void GroupsToModel(AxesModel axes, List<GroupDto>? dtos)
    {
        if (dtos is not { Count: > 0 })
        {
            return;
        }

        // Every group first, so a member that is a group made later than its outer one is there to
        // be named; then each group's members in the order they joined.
        var made = new List<GraphicsGroupModel>(dtos.Count);
        foreach (GroupDto dto in dtos)
        {
            GraphicsGroupModel? outer = dto.Outer >= 0 && dto.Outer < made.Count ? made[dto.Outer] : null;
            var group = new GraphicsGroupModel(dto.Transforms, axes, outer, joinOuter: false)
            {
                Tag = dto.Tag,
                Visible = dto.Visible,
                DisplayName = dto.DisplayName ?? string.Empty,
            };

            if (dto.Transforms && dto.Matrix is { Length: 16 } rows)
            {
                var matrix = new double[4, 4];
                for (int i = 0; i < 16; i++)
                {
                    matrix[i / 4, i % 4] = rows[i];
                }

                group.SetMatrix(matrix);
            }

            made.Add(group);
        }

        for (int i = 0; i < dtos.Count; i++)
        {
            foreach (GroupMemberDto named in dtos[i].Members)
            {
                if (Resolve(axes, made, named) is { } member && !ReferenceEquals(member, made[i]))
                {
                    made[i].Admit(member);
                }
            }

            made[i].Seat = dtos[i].Before is { } before && Resolve(axes, made, before) is { } next
                ? next.CreationOrder
                : long.MaxValue;
            axes.AddGroup(made[i]);
        }
    }

    private static GroupMemberDto? MemberRef(AxesModel axes, IReadOnlyList<GraphicsGroupModel> groups, GraphObject member)
    {
        (string kind, int index) = member switch
        {
            GraphicsGroupModel inner => ("group", IndexOf(groups, inner)),
            PlotObject plot => ("plot", axes.Plots.IndexOf(plot)),
            AnnotationObject text => ("text", axes.Annotations.IndexOf(text)),
            LightModel light => ("light", axes.Lights.IndexOf(light)),
            _ => ("", -1),
        };

        // A member deleted since it joined has left its axes, and is not written.
        return index < 0 ? null : new GroupMemberDto { Kind = kind, Index = index };
    }

    private static GraphObject? Resolve(AxesModel axes, List<GraphicsGroupModel> groups, GroupMemberDto named) => named.Kind switch
    {
        "group" when named.Index >= 0 && named.Index < groups.Count => groups[named.Index],
        "plot" when named.Index >= 0 && named.Index < axes.Plots.Count => axes.Plots[named.Index],
        "text" when named.Index >= 0 && named.Index < axes.Annotations.Count => axes.Annotations[named.Index],
        "light" when named.Index >= 0 && named.Index < axes.Lights.Count => axes.Lights[named.Index],
        _ => null,
    };

    private static int IndexOf(IReadOnlyList<GraphicsGroupModel> groups, GraphicsGroupModel group)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (ReferenceEquals(groups[i], group))
            {
                return i;
            }
        }

        return -1;
    }

    private static double[] RowMajor(double[,] matrix)
    {
        var rows = new double[16];
        for (int i = 0; i < 16; i++)
        {
            rows[i] = matrix[i / 4, i % 4];
        }

        return rows;
    }
}
