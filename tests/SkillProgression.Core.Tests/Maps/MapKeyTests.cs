using SkillProgression.Core.Maps;

namespace SkillProgression.Core.Tests.Maps;

public class MapKeyTests
{
    [Fact]
    public void Hash_is_normalised_to_upper_case_and_trimmed()
    {
        var key = new MapKey(" 33f7950e5d92996727d514c78d5d30242d371761 ", "Standard", "ExpertPlus");
        Assert.Equal("33F7950E5D92996727D514C78D5D30242D371761", key.Hash);
    }

    [Fact]
    public void Keys_differing_only_by_hash_case_are_equal()
    {
        var a = new MapKey("abc123", "Standard", "ExpertPlus");
        var b = new MapKey("ABC123", "standard", "expertplus");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Same_hash_with_different_difficulty_or_characteristic_is_a_different_map()
    {
        var expertPlus = new MapKey("abc123", "Standard", "ExpertPlus");
        Assert.NotEqual(expertPlus, new MapKey("abc123", "Standard", "Expert"));
        Assert.NotEqual(expertPlus, new MapKey("abc123", "OneSaber", "ExpertPlus"));
    }

    [Fact]
    public void Missing_characteristic_defaults_to_standard()
    {
        Assert.Equal("Standard", new MapKey("abc", "", "Hard").Characteristic);
    }

    [Theory]
    [InlineData("", "Hard")]
    [InlineData("abc", " ")]
    public void Hash_and_difficulty_are_required(string hash, string difficulty)
    {
        Assert.Throws<ArgumentException>(() => new MapKey(hash, "Standard", difficulty));
    }
}
