using Dalan.Domain.Deliveries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dalan.Infrastructure.Persistence.Configurations;

public sealed class DeliveryProofConfiguration : IEntityTypeConfiguration<DeliveryProof>
{
    public void Configure(EntityTypeBuilder<DeliveryProof> builder)
    {
        builder.ToTable("DeliveryProofs");
        builder.HasKey(proof => proof.Id);
        builder.Property(proof => proof.Id).ValueGeneratedNever();
        builder.Property(proof => proof.PhotoReference)
            .HasMaxLength(DeliveryProof.MaxPhotoReferenceLength).IsRequired();
        builder.Property(proof => proof.RecipientConfirmation)
            .HasMaxLength(DeliveryProof.MaxRecipientConfirmationLength).IsRequired();
        builder.Property(proof => proof.CapturedAtUtc).IsRequired();
        builder.HasIndex(proof => proof.DeliveryId).IsUnique();
    }
}
