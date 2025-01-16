using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicEnchanter : MimicNPC
    //{
    //    public MimicEnchanter(byte level) : base(new ClassEnchanter(), level)
    //    { }
    //}

    public class EnchanterSpec : MimicSpec
    {
        public EnchanterSpec(eSpecType spec)
        {
            SpecName = "EnchanterSpec";

            WeaponOneType = eObjectType.Staff;
            Is2H = true;

            var randVariance = spec switch
            {
                eSpecType.ManaEnchanter => Util.Random(0, 1),
                eSpecType.LightEnchanter => Util.Random(2, 3),
                _ => Util.Random(3),
            };

            switch (randVariance)
            {
                case 0:
                SpecType = eSpecType.ManaEnchanter;
                Add(Specs.Mana, 50, 1.0f);
                Add(Specs.Light, 20, 0.1f);
                Add(Specs.Enchantments, 4, 0.0f);
                break;

                case 1:
                SpecType = eSpecType.ManaEnchanter;
                Add(Specs.Mana, 49, 1.0f);
                Add(Specs.Light, 22, 0.1f);
                Add(Specs.Enchantments, 5, 0.0f);
                break;

                case 2:
                SpecType = eSpecType.LightEnchanter;
                Add(Specs.Mana, 27, 0.2f);
                Add(Specs.Light, 45, 1.0f);
                Add(Specs.Enchantments, 12, 0.1f);
                break;

                case 3:
                SpecType = eSpecType.LightEnchanter;
                Add(Specs.Mana, 24, 0.2f);
                Add(Specs.Light, 45, 1.0f);
                Add(Specs.Enchantments, 17, 0.1f);
                break;
            }
        }
    }
}